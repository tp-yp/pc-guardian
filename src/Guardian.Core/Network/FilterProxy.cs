using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Guardian.Core.Config;
using Guardian.Core.Policy;

namespace Guardian.Core.Network;

/// <summary>
/// Local HTTP/HTTPS filtering proxy. HTTPS is decrypted with a per-machine CA so the full URL
/// can be checked; hosts in <see cref="NetworkPolicy.NoDecryptDomains"/> are tunnelled as-is and
/// filtered by host only. Upstream certificates are validated normally, so a bad site certificate
/// still fails (as a 502) instead of being hidden by the proxy.
/// One request per connection (Connection: close) keeps the relay simple and correct.
/// </summary>
public sealed class FilterProxy(
    IPEndPoint listen,
    LocalCertificateAuthority ca,
    Func<NetworkPolicy> policy,
    Action<string, string, bool>? log = null,
    RemoteCertificateValidationCallback? upstreamValidation = null)
{
    private TcpListener? _listener;

    public int Port => ((IPEndPoint)_listener!.LocalEndpoint).Port;

    public void Start()
    {
        _listener = new TcpListener(listen);
        _listener.Start(512);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (_listener is null) Start();
        using var reg = ct.Register(() => _listener!.Stop());
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener!.AcceptTcpClientAsync(ct); }
            catch (Exception) when (ct.IsCancellationRequested) { break; }
            catch (SocketException) { continue; }
            _ = Task.Run(() => Handle(client, ct), ct);
        }
    }

    private async Task Handle(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        client.NoDelay = true;
        Stream clientStream = client.GetStream();
        try
        {
            var (head, rest) = await ReadHead(clientStream, ct);
            if (head is null) return;
            var (host, port, path) = head.Resolve(tls: false);
            if (host.Length == 0) return;
            var p = policy();

            if (head.Method != "CONNECT")
            {
                await Forward(clientStream, head, rest, host, port, path, tls: false, ct);
                return;
            }

            await clientStream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), ct);

            if (p.NoDecryptDomains.Any(d => UrlPolicy.HostMatches(host, d)))
            {
                var ok = UrlPolicy.IsHostAllowed(p, host);
                log?.Invoke(host, host + " (tunnel)", ok);
                if (!ok) return;
                using var up = await ConnectTcp(host, port, ct);
                await Pipe(clientStream, up.GetStream(), rest, ct);
                return;
            }

            await using var tls = new SslStream(clientStream, leaveInnerStreamOpen: true);
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = ca.GetLeaf(host),
                ApplicationProtocols = [SslApplicationProtocol.Http11],
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, ct);

            var (inner, innerRest) = await ReadHead(tls, ct);
            if (inner is null) return;
            var (_, _, innerPath) = inner.Resolve(tls: true);
            await Forward(tls, inner, innerRest, host, port, innerPath, tls: true, ct);
        }
        catch (Exception e) when (e is IOException or SocketException or AuthenticationException or OperationCanceledException or ObjectDisposedException)
        {
            // Client went away, TLS failed, or upstream unreachable: nothing to report to the client.
        }
    }

    private async Task Forward(Stream client, HttpHead head, byte[] rest, string host, int port, string path, bool tls, CancellationToken ct)
    {
        var url = (tls ? "https://" : "http://") + host + (port is 80 or 443 ? "" : ":" + port) + path;
        var allowed = UrlPolicy.IsUrlAllowed(policy(), host, path);
        log?.Invoke(host, url, allowed);
        if (!allowed)
        {
            await client.WriteAsync(HttpHead.BlockedResponse(url), ct);
            return;
        }

        TcpClient upTcp;
        try { upTcp = await ConnectTcp(host, port, ct); }
        catch (SocketException)
        {
            await client.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 502 Bad Gateway\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct);
            return;
        }
        using (upTcp)
        {
            Stream up = upTcp.GetStream();
            if (tls)
            {
                var ssl = new SslStream(up, leaveInnerStreamOpen: false);
                try
                {
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = host,
                        ApplicationProtocols = [SslApplicationProtocol.Http11],
                        RemoteCertificateValidationCallback = upstreamValidation,
                    }, ct);
                }
                catch (AuthenticationException)
                {
                    await client.WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 502 Bad Gateway\r\nContent-Type: text/plain\r\nContent-Length: 34\r\nConnection: close\r\n\r\nUpstream certificate is not valid."), ct);
                    return;
                }
                up = ssl;
            }
            await up.WriteAsync(head.ToUpstreamBytes(path), ct);
            await Pipe(client, up, rest, ct);
        }
    }

    private static async Task<TcpClient> ConnectTcp(string host, int port, CancellationToken ct)
    {
        var c = new TcpClient { NoDelay = true };
        try { await c.ConnectAsync(host, port, ct); return c; }
        catch { c.Dispose(); throw; }
    }

    private static async Task Pipe(Stream client, Stream upstream, byte[] pending, CancellationToken ct)
    {
        if (pending.Length > 0) await upstream.WriteAsync(pending, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var a = client.CopyToAsync(upstream, cts.Token);
        var b = upstream.CopyToAsync(client, cts.Token);
        await Task.WhenAny(a, b);
        // Give the other direction a moment to flush (e.g. response after request body finished).
        await Task.WhenAny(Task.WhenAll(a, b), Task.Delay(TimeSpan.FromSeconds(a.IsCompleted ? 30 : 1), ct));
        cts.Cancel();
    }

    /// <summary>Reads an HTTP head; returns it and any bytes already read past it.</summary>
    public static async Task<(HttpHead? Head, byte[] Remaining)> ReadHead(Stream s, CancellationToken ct)
    {
        var buf = new byte[HttpHead.MaxSize];
        var len = 0;
        while (len < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(len), ct);
            if (n == 0) return (null, []);
            len += n;
            var end = HttpHead.FindEnd(buf.AsSpan(0, len));
            if (end >= 0) return (HttpHead.Parse(buf.AsSpan(0, end)), buf[end..len]);
        }
        return (null, []);
    }
}
