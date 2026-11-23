using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Guardian.Core.Config;
using Guardian.Core.Network;

namespace Guardian.Core.Tests;

/// <summary>End-to-end: client -> proxy (MITM) -> fake HTTPS origin, all on loopback.</summary>
public sealed class FilterProxyTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _cts = new();
    private LocalCertificateAuthority _proxyCa = null!, _originCa = null!;
    private X509Certificate2 _proxyRoot = null!, _originRoot = null!;
    private TcpListener _origin = null!;
    private FilterProxy _proxy = null!;
    private readonly NetworkPolicy _policy = new() { Domains = { "localhost/blocked" } };
    private readonly List<(string Url, bool Allowed)> _log = new();

    public Task InitializeAsync()
    {
        _proxyRoot = LocalCertificateAuthority.CreateRoot();
        _originRoot = LocalCertificateAuthority.CreateRoot();
        _proxyCa = new LocalCertificateAuthority(_proxyRoot);
        _originCa = new LocalCertificateAuthority(_originRoot);

        _origin = new TcpListener(IPAddress.Loopback, 0);
        _origin.Start();
        _ = ServeOrigin(_cts.Token);

        _proxy = new FilterProxy(new IPEndPoint(IPAddress.Loopback, 0), _proxyCa, () => _policy,
            (h, url, ok) => { lock (_log) _log.Add((url, ok)); },
            upstreamValidation: (_, cert, _, _) => Trusts(_originRoot, cert));
        _proxy.Start();
        _ = _proxy.RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public Task DisposeAsync() { _cts.Cancel(); _origin.Stop(); return Task.CompletedTask; }

    private int OriginPort => ((IPEndPoint)_origin.LocalEndpoint).Port;

    private static bool Trusts(X509Certificate2 root, X509Certificate? cert)
    {
        if (cert is null) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(new X509Certificate2(cert));
    }

    private async Task ServeOrigin(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await _origin.AcceptTcpClientAsync(ct); } catch { return; }
            _ = Task.Run(async () =>
            {
                using (c)
                await using (var ssl = new SslStream(c.GetStream()))
                {
                    await ssl.AuthenticateAsServerAsync(_originCa.GetLeaf("localhost"));
                    var (head, _) = await FilterProxy.ReadHead(ssl, ct);
                    var body = $"origin saw {head!.Method} {head.Target} conn={head.Get("Connection")}";
                    await ssl.WriteAsync(Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}"), ct);
                }
            }, ct);
        }
    }

    private async Task<string> GetViaProxy(string path)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, _proxy.Port);
        var s = tcp.GetStream();
        await s.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT localhost:{OriginPort} HTTP/1.1\r\nHost: localhost:{OriginPort}\r\n\r\n"));
        await FilterProxy.ReadHead(s, default); // consumes "HTTP/1.1 200 Connection Established"
        await using var ssl = new SslStream(s);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = "localhost",
            RemoteCertificateValidationCallback = (_, cert, _, _) => Trusts(_proxyRoot, cert),
        });
        await ssl.WriteAsync(Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: localhost\r\nConnection: keep-alive\r\n\r\n"));
        using var reader = new StreamReader(ssl);
        return await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Allowed_url_reaches_origin_with_connection_close()
    {
        var text = await GetViaProxy("/ok?x=1");
        Assert.Contains("200 OK", text);
        Assert.Contains("origin saw GET /ok?x=1 conn=close", text);
        lock (_log) Assert.Contains(_log, e => e.Url.EndsWith("/ok?x=1") && e.Allowed);
    }

    [Fact]
    public async Task Blocked_path_gets_block_page_and_never_reaches_origin()
    {
        var text = await GetViaProxy("/blocked/page");
        Assert.Contains("403 Forbidden", text);
        Assert.DoesNotContain("origin saw", text);
        lock (_log) Assert.Contains(_log, e => e.Url.EndsWith("/blocked/page") && !e.Allowed);
    }

    [Fact]
    public async Task Invalid_upstream_certificate_is_not_hidden()
    {
        var strict = new FilterProxy(new IPEndPoint(IPAddress.Loopback, 0), _proxyCa, () => _policy); // default validation
        strict.Start();
        _ = strict.RunAsync(_cts.Token);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, strict.Port);
        var s = tcp.GetStream();
        await s.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT localhost:{OriginPort} HTTP/1.1\r\n\r\n"));
        await FilterProxy.ReadHead(s, default);
        await using var ssl = new SslStream(s);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            { TargetHost = "localhost", RemoteCertificateValidationCallback = (_, c, _, _) => Trusts(_proxyRoot, c) });
        await ssl.WriteAsync("GET / HTTP/1.1\r\nHost: localhost\r\n\r\n"u8.ToArray());
        var text = await new StreamReader(ssl).ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("502", text);
    }
}
