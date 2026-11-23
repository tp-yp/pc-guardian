using System.Security.Cryptography.X509Certificates;
using Guardian.Core.Network;

namespace Guardian.Core.Tests;

public class CertificateTests
{
    private static readonly Lazy<X509Certificate2> Root = new(() => LocalCertificateAuthority.CreateRoot());

    [Fact]
    public void Leaf_chains_to_root_and_names_host()
    {
        using var ca = new LocalCertificateAuthority(new X509Certificate2(Root.Value));
        var leaf = ca.GetLeaf("www.example.com");
        Assert.True(leaf.HasPrivateKey);
        Assert.Equal("www.example.com", leaf.GetNameInfo(X509NameType.DnsName, false));

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(Root.Value);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        Assert.True(chain.Build(leaf), string.Join(",", chain.ChainStatus.Select(s => s.Status)));
    }

    [Fact]
    public void Leaf_is_cached_per_host()
    {
        using var ca = new LocalCertificateAuthority(new X509Certificate2(Root.Value));
        Assert.Same(ca.GetLeaf("a.com"), ca.GetLeaf("A.com"));
        Assert.NotSame(ca.GetLeaf("a.com"), ca.GetLeaf("b.com"));
    }

    [Fact]
    public void Root_is_a_ca_and_unique()
    {
        Assert.Contains(Root.Value.Extensions.OfType<X509BasicConstraintsExtension>(), e => e.CertificateAuthority);
        using var other = LocalCertificateAuthority.CreateRoot();
        Assert.NotEqual(Root.Value.Thumbprint, other.Thumbprint);
    }
}
