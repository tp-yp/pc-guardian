using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Guardian.Core.Config;
using Guardian.Core.Network;

namespace Guardian.Service.Enforcers;

/// <summary>
/// Runs the full-URL filtering proxy on 127.0.0.1:8899 and owns the per-machine root CA:
/// created on first start, private key stored DPAPI-protected in the SYSTEM-only data folder,
/// certificate installed into LocalMachine\Root so browsers trust the proxy's certificates.
/// </summary>
public sealed class ProxyHost(ILogger<ProxyHost> log, AccessLog accessLog, IspFilterMonitor isp) : BackgroundService
{
    public const int Port = 8899;
    private static readonly byte[] Entropy = "Guardian.ProxyCA.v1"u8.ToArray();
    private static string CaPath => Path.Combine(Paths.DataDir, "ca.bin");
    private volatile NetworkPolicy _policy = new() { Enabled = false };
    private volatile GuardianConfig _cfg = new();

    private static readonly NetworkPolicy NoFilterPolicy = new() { Enabled = true, Mode = UrlMode.Allowlist };

    public void Update(GuardianConfig cfg) { _cfg = cfg; _policy = cfg.Network; }

    /// <summary>
    /// The program behind a proxy connection is found from the connection's local port; its Windows
    /// account decides which rules apply (general rules if it has none of its own, or is unknown).
    /// </summary>
    private ProxyClient Identify(IPEndPoint client)
    {
        var cfg = _cfg;
        // Not on the filtered line: Guardian itself may still go out (it keeps checking), so the proxy
        // must refuse everything. An empty allow-list blocks every address.
        if (isp.Isolated(cfg))
            return new ProxyClient(cfg.Network.LocalAddresses.Count == 0 ? NoFilterPolicy
                : new NetworkPolicy { Enabled = true, Mode = UrlMode.Allowlist, LocalAddresses = cfg.Network.LocalAddresses }, "");
        var pid = Native.ProcessUser.OwnerOfLoopbackConnection(client.Port, Port, client.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);
        var user = pid > 0 ? Native.ProcessUser.Of(pid) : null;
        return new ProxyClient(Guardian.Core.Policy.UserScopes.Effective(cfg, user).Network, user ?? "");
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Nothing (no CA, no trusted root) until the owner turns URL filtering on.
        while (!(_policy.Enabled && _policy.UrlFiltering)) await Task.Delay(TimeSpan.FromSeconds(3), ct);

        LocalCertificateAuthority ca;
        try { ca = new LocalCertificateAuthority(LoadOrCreateRoot()); }
        catch (Exception ex) { log.LogError(ex, "Proxy CA unavailable; URL filtering disabled"); return; }

        using (ca)
        {
            var proxy = new FilterProxy(new IPEndPoint(IPAddress.Loopback, Port), ca, () => _policy,
                (user, url, ok) => accessLog.Add("web", url, ok, user), blockPage: url => isp.Isolated(_cfg) ? Ui.BlockScreen.NoFilter(url) : Ui.BlockScreen.ForWebPage(url), identify: Identify);
            try { proxy.Start(); }
            catch (Exception ex) { log.LogError(ex, "Cannot listen on port {Port}", Port); return; }
            log.LogInformation("Filtering proxy listening on 127.0.0.1:{Port}", Port);
            await proxy.RunAsync(ct);
        }
    }

    private X509Certificate2 LoadOrCreateRoot()
    {
        X509Certificate2 root;
        if (File.Exists(CaPath))
        {
            var pfx = ProtectedData.Unprotect(File.ReadAllBytes(CaPath), Entropy, DataProtectionScope.LocalMachine);
            root = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet);
        }
        else
        {
            using var created = LocalCertificateAuthority.CreateRoot();
            var pfx = created.Export(X509ContentType.Pkcs12);
            File.WriteAllBytes(CaPath, ProtectedData.Protect(pfx, Entropy, DataProtectionScope.LocalMachine));
            root = new X509Certificate2(pfx, (string?)null, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet);
            log.LogInformation("Created proxy root CA {Thumb}", root.Thumbprint);
        }
        EnsureTrusted(root);
        return root;
    }

    /// <summary>Installs only the public certificate (never the key) into LocalMachine\Root.</summary>
    private static void EnsureTrusted(X509Certificate2 root)
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        if (store.Certificates.Find(X509FindType.FindByThumbprint, root.Thumbprint, false).Count == 0)
            store.Add(new X509Certificate2(root.Export(X509ContentType.Cert)));
    }

    /// <summary>Uninstall: remove every Guardian root from the trust store and delete the key.</summary>
    public static void RemoveCa()
    {
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);
        foreach (var c in store.Certificates.Find(X509FindType.FindBySubjectDistinguishedName, LocalCertificateAuthority.SubjectName, false))
            store.Remove(c);
        if (File.Exists(CaPath)) File.Delete(CaPath);
    }
}
