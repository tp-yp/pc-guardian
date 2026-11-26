using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Guardian.Core.Config;
using Guardian.Core.Network;

namespace Guardian.Service.Enforcers;

/// <summary>
/// "Internet only through the filter" (NetFree / Etrog / NetSpark): opens TLS connections to a few
/// well-known sites straight from the service (not through Guardian's proxy) and looks at who signed
/// the certificate. A filtering provider re-signs sites with its own certificate, so its name (or a
/// pinned thumbprint) in the chain means the line is filtered. Checked every minute, sooner while
/// blocked, and a few seconds after any network change. Anything but a confirmed filter blocks.
/// </summary>
public sealed class IspFilterMonitor : IDisposable
{
    private readonly ILogger<IspFilterMonitor> _log;
    private readonly AccessLog _accessLog;
    private readonly object _gate = new();
    private List<(string Host, List<ChainCert>? Chain, string? Error)> _probes = [];
    private IspFilterResult _last = new(IspFilterStatus.Unknown, DateTime.Now, "", []);
    private DateTime _nextCheck = DateTime.MinValue;
    private int _running;
    private bool? _lastBlocked;

    public IspFilterMonitor(ILogger<IspFilterMonitor> log, AccessLog accessLog)
    {
        _log = log;
        _accessLog = accessLog;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    public IspFilterResult Last { get { lock (_gate) return _last; } }

    /// <summary>True when the switch is on and the filter is not confirmed right now.</summary>
    public bool Isolated(GuardianConfig cfg) => cfg.IspFilter.Required && Last.ShouldBlock;

    private DateTime _fastUntil = DateTime.MinValue;

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            // Check soon, and every few seconds for a while, so a filtered network opens quickly.
            _nextCheck = DateTime.UtcNow.AddSeconds(2);
            _fastUntil = DateTime.UtcNow.AddSeconds(90);
            // A network appeared after being offline: blocked until a check confirms the filter.
            if (_last.Status == IspFilterStatus.Offline && HasNetwork())
                _last = new(IspFilterStatus.Unknown, DateTime.Now, "network came back, checking", []);
        }
    }

    /// <summary>True when some network adapter is connected with a gateway (a way out to other networks).</summary>
    private static bool HasNetwork()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up &&
                n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) &&
                n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any)));
        }
        catch (Exception) { return true; } // unknown: stay strict
    }

    /// <summary>
    /// Called by the main loop every few seconds. Re-decides with the current settings (so a newly
    /// trusted certificate counts at once), starts a check when one is due, and returns true when
    /// the block state changed since the last call.
    /// </summary>
    public bool Tick(GuardianConfig cfg)
    {
        if (!cfg.IspFilter.Required) { _lastBlocked = null; return false; }
        bool due;
        lock (_gate)
        {
            if (!HasNetwork()) _last = new(IspFilterStatus.Offline, DateTime.Now, "no network", []);
            else if (_last.Status == IspFilterStatus.Offline) _last = new(IspFilterStatus.Unknown, DateTime.Now, "network came back, checking", []);
            else if (_probes.Count > 0 && _last.Status != IspFilterStatus.Unknown) _last = IspFilterCheck.Decide(cfg.IspFilter, _probes, _last.Time);
            due = DateTime.UtcNow >= _nextCheck;
        }
        if (due && Last.Status != IspFilterStatus.Offline) _ = CheckAsync(cfg.IspFilter);
        var blocked = Last.ShouldBlock;
        var changed = _lastBlocked != blocked;
        if (changed && _lastBlocked != null)
        {
            _accessLog.Add("isp", blocked ? "אין חיבור דרך הסינון - האינטרנט נחסם" : "החיבור עובר דרך הסינון - האינטרנט פתוח", !blocked);
            _log.LogWarning("Filtered-line check: {Status} ({Detail})", Last.Status, Last.Detail);
        }
        _lastBlocked = blocked;
        return changed;
    }

    /// <summary>Runs one round of checks (skipped if one is already running) and returns the result.</summary>
    public async Task<IspFilterResult> CheckAsync(IspFilterPolicy policy)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return Last;
        try
        {
            lock (_gate) _nextCheck = DateTime.UtcNow.AddSeconds(60);
            var probes = (await Task.WhenAll(IspFilterCheck.ProbeHosts.Select(Probe))).ToList();
            lock (_gate)
            {
                _probes = probes;
                _last = IspFilterCheck.Decide(policy, probes, DateTime.Now);
                // While blocked, look again sooner so the internet comes back quickly once filtered.
                _nextCheck = DateTime.UtcNow.AddSeconds(!_last.ShouldBlock ? 60 : DateTime.UtcNow < _fastUntil ? 3 : 20);
                return _last;
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Filtered-line check failed");
            return Last;
        }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    private static async Task<(string Host, List<ChainCert>? Chain, string? Error)> Probe(string host)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, 443, cts.Token);
            List<ChainCert>? chain = null;
            await using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, x509Chain, _) =>
            {
                chain = Capture(cert, x509Chain);
                return true; // only reading who signed it; no data is sent over this connection
            });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cts.Token);
            return (host, chain, chain is { Count: > 0 } ? null : "no certificate");
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
            return (host, null, ex is OperationCanceledException ? $"{host}: timeout" : $"{host}: {ex.Message}");
        }
    }

    private static List<ChainCert> Capture(X509Certificate? cert, X509Chain? chain)
    {
        var list = new List<ChainCert>();
        if (chain != null)
            foreach (var el in chain.ChainElements)
                list.Add(new(el.Certificate.Subject, el.Certificate.Issuer, el.Certificate.Thumbprint));
        if (list.Count == 0 && cert != null)
        {
            using var c = new X509Certificate2(cert);
            list.Add(new(c.Subject, c.Issuer, c.Thumbprint));
        }
        return list;
    }

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
    }
}
