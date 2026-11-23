using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Guardian.Core.Config;
using Guardian.Core.Network;

namespace Guardian.Service.Enforcers;

/// <summary>
/// Local DNS resolver on 127.0.0.1:53 and [::1]:53. Blocked names get 0.0.0.0 / :: / NXDOMAIN;
/// everything else is resolved upstream over DNS-over-HTTPS (by IP, so it needs no DNS itself
/// and is not affected by the port-53 firewall block).
/// </summary>
public sealed class DnsFilterServer(ILogger<DnsFilterServer> log, AccessLog accessLog) : BackgroundService
{
    private static readonly string[] Upstreams = ["https://1.1.1.1/dns-query", "https://9.9.9.9/dns-query"];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private volatile NetworkPolicy _policy = new() { Enabled = false };

    public void Update(GuardianConfig cfg) => _policy = cfg.Network;

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
        if (!DnsFilter.IsAllowed(_policy, q.Name))
        {
            accessLog.Add("dns", q.Name, allowed: false);
            return DnsMessage.BuildBlocked(query);
        }
        accessLog.Add("dns", q.Name, allowed: true);
        foreach (var url in Upstreams)
        {
            try
            {
                using var content = new ByteArrayContent(query);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
                using var resp = await _http.PostAsync(url, content, ct);
                if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsByteArrayAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { /* try next */ }
        }
        return null;
    }
}
