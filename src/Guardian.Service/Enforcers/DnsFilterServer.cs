using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Guardian.Core.Config;
using Guardian.Core.Network;

namespace Guardian.Service.Enforcers;

/// <summary>
/// Local DNS resolver on 127.0.0.1:53 and [::1]:53. Blocked names get 0.0.0.0 / :: / NXDOMAIN;
/// everything else is resolved upstream over DNS-over-HTTPS (by IP, so it needs no DNS itself
/// and is not affected by the port-53 firewall block). Internal names of the connected network are asked
/// from that network's own DNS server (see <see cref="NetworkDns"/>).
/// </summary>
public sealed class DnsFilterServer(ILogger<DnsFilterServer> log, AccessLog accessLog) : BackgroundService
{
    private static readonly string[] Upstreams =
        ["https://1.1.1.1/dns-query", "https://9.9.9.9/dns-query", "https://8.8.8.8/dns-query", "https://1.0.0.1/dns-query", "https://149.112.112.112/dns-query"];
    private readonly HttpClient _http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), UseProxy = false })
        { Timeout = TimeSpan.FromSeconds(3) };
    private readonly DnsCache _cache = new();
    public string LastUpstreamError { get; private set; } = "";
    public DateTime LastUpstreamSuccess { get; private set; }
    private volatile GuardianConfig _cfg = new() { Network = new NetworkPolicy { Enabled = false } };

    public void Update(GuardianConfig cfg) => _cfg = cfg;

    protected override Task ExecuteAsync(CancellationToken ct) => Task.WhenAll(
        Serve(new IPEndPoint(IPAddress.Loopback, 53), ct),
        Serve(new IPEndPoint(IPAddress.IPv6Loopback, 53), ct));

    private async Task Serve(IPEndPoint ep, CancellationToken ct)
    {
        UdpClient udp;
        try { udp = new UdpClient(ep); }
        catch (SocketException ex) { log.LogError(ex, "Cannot bind DNS on {Ep}", ep); return; }
        using (udp)
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult r;
                try { r = await udp.ReceiveAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (SocketException) { continue; } // ICMP port unreachable from a previous reply
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var reply = await Answer(r.Buffer, ct);
                        if (reply != null) await udp.SendAsync(reply, r.RemoteEndPoint, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { log.LogDebug(ex, "DNS query failed"); }
                }, ct);
            }
        }
    }

    private async Task<byte[]?> Answer(byte[] query, CancellationToken ct)
    {
        var q = DnsMessage.ReadQuestion(query);
        if (q is null) return null;
        // Names only the connected network knows (a school's server, a printer): ask the network's own DNS.
        // Names that can never be internet sites skip the site filter; names under the network's suffix do not.
        var net = NetworkDns.Current();
        if (net.Servers.Count > 0 && InternalNames.IsInternal(q.Name, net.Suffixes))
        {
            if (InternalNames.IsPrivateOnly(q.Name) || Guardian.Core.Policy.UserScopes.DnsAllows(_cfg, q.Name))
            {
                if (await NetworkDns.AskAsync(net.Servers, query, ct) is { } local)
                {
                    accessLog.Add("dns", q.Name, allowed: true);
                    return local;
                }
            }
        }
        // DNS cannot tell which account asks: only names blocked for everyone are refused here.
        if (!Guardian.Core.Policy.UserScopes.DnsAllows(_cfg, q.Name))
        {
            accessLog.Add("dns", q.Name, allowed: false);
            return DnsMessage.BuildBlocked(query);
        }
        accessLog.Add("dns", q.Name, allowed: true);
        if (_cache.Get(query, q) is { } cached) return cached;

        // Ask two upstreams at once and take the first good answer; then the rest, one by one.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var first = new[] { Ask(Upstreams[0], query, cts.Token), Ask(Upstreams[1], query, cts.Token) }.ToList();
        while (first.Count > 0)
        {
            var done = await Task.WhenAny(first);
            first.Remove(done);
            if (await done is { } answer) { cts.Cancel(); return Remember(q, answer); }
        }
        foreach (var url in Upstreams.Skip(2))
            if (await Ask(url, query, ct) is { } answer) return Remember(q, answer);

        // Every upstream failed: an old answer is better than no internet; otherwise tell the client to retry.
        return _cache.Get(query, q, allowStale: true) ?? DnsMessage.BuildServFail(query);
    }

    private byte[] Remember(DnsMessage.Question q, byte[] answer)
    {
        _cache.Put(q, answer);
        LastUpstreamSuccess = DateTime.UtcNow;
        return answer;
    }

    private async Task<byte[]?> Ask(string url, byte[] query, CancellationToken ct)
    {
        try
        {
            using var content = new ByteArrayContent(query);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
            using var resp = await _http.PostAsync(url, content, ct);
            if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsByteArrayAsync(ct);
            LastUpstreamError = $"{url}: HTTP {(int)resp.StatusCode}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (!ct.IsCancellationRequested) { LastUpstreamError = $"{url}: {ex.Message}"; log.LogDebug("DNS upstream {Url} failed: {Msg}", url, ex.Message); }
        }
        return null;
    }
}
