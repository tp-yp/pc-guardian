using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Guardian.Core.Network;

/// <summary>
/// Per-machine root CA used by the filtering proxy to present certificates for HTTPS sites.
/// The root is unique to each install, so it cannot be used against any other computer.
/// </summary>
public sealed class LocalCertificateAuthority : IDisposable
{
    public const string SubjectName = "CN=Guardian Local Filter CA, O=Guardian (this computer only)";
    private readonly X509Certificate2 _root;
    private readonly ConcurrentDictionary<string, X509Certificate2> _leafCache = new(StringComparer.OrdinalIgnoreCase);

    public LocalCertificateAuthority(X509Certificate2 rootWithKey)
    {
        if (!rootWithKey.HasPrivateKey) throw new ArgumentException("Root needs its private key.");
        _root = rootWithKey;
    }

    public X509Certificate2 Root => _root;

    public static X509Certificate2 CreateRoot(int years = 10)
    {
        using var key = RSA.Create(3072);
        var req = new CertificateRequest(SubjectName, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(years));
    }

    /// <summary>Leaf certificate for a host (cached). Short-lived, ECDSA P-256 for speed.</summary>
    public X509Certificate2 GetLeaf(string host)
    {
        if (_leafCache.TryGetValue(host, out var c) && c.NotAfter > DateTime.Now.AddDays(1)) return c;
        return _leafCache[host] = IssueLeaf(host);
    }

    private X509Certificate2 IssueLeaf(string host)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest($"CN={Sanitize(host)}", key, HashAlgorithmName.SHA256);
        var san = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(host, out var ip)) san.AddIpAddress(ip); else san.AddDnsName(host);
        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(_root, true, false));

        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        var notAfter = DateTimeOffset.UtcNow.AddDays(30);
        if (notAfter > _root.NotAfter) notAfter = _root.NotAfter;
        using var rootKey = _root.GetRSAPrivateKey()!;
        var signer = X509SignatureGenerator.CreateForRSA(rootKey, RSASignaturePadding.Pkcs1);
        using var pub = req.Create(_root.SubjectName, signer, DateTimeOffset.UtcNow.AddDays(-1), notAfter, serial);
        using var withKey = pub.CopyWithPrivateKey(key);
        // Re-import so SslStream on Windows can use the ephemeral key.
        return new X509Certificate2(withKey.Export(X509ContentType.Pkcs12), (string?)null, X509KeyStorageFlags.Exportable);
    }

    private static string Sanitize(string host) => new(host.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or ':').ToArray());

    public void Dispose()
    {
        foreach (var c in _leafCache.Values) c.Dispose();
        _root.Dispose();
    }
}
