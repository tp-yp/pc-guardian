using Guardian.Core.Config;

namespace Guardian.Core.Network;

/// <summary>One certificate of a TLS chain, from the site's certificate up to the root.</summary>
public sealed record ChainCert(string Subject, string Issuer, string Thumbprint);

public enum IspFilterStatus { Unknown, Filtered, NotFiltered, NoNetwork }

/// <summary>Result of one round of checks, shown in the UI and used to block or unblock.</summary>
public sealed record IspFilterResult(IspFilterStatus Status, DateTime Time, string Detail, List<ChainCert> Chain)
{
    /// <summary>Fail closed: anything but a confirmed filter keeps the internet blocked.</summary>
    public bool ShouldBlock => Status != IspFilterStatus.Filtered;
}

public static class IspFilterCheck
{
    /// <summary>Well-known HTTPS sites to probe. Several, so one site being down is not "no filter".</summary>
    public static readonly string[] ProbeHosts = ["www.google.com", "www.microsoft.com", "www.wikipedia.org", "www.cloudflare.com"];

    /// <summary>True if the chain was signed by the filter: a pinned thumbprint, or a configured name in any subject/issuer.</summary>
    public static bool IsFilterChain(IspFilterPolicy policy, IReadOnlyList<ChainCert> chain)
    {
        if (chain.Count == 0) return false;
        var pins = policy.Thumbprints.Select(Normalize).Where(t => t.Length > 0).ToHashSet();
        if (chain.Any(c => pins.Contains(Normalize(c.Thumbprint)))) return true;
        var names = policy.IssuerNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        // The site's own certificate names the site; only issuers (and certs above the leaf) identify the signer.
        return chain.Select((c, i) => i == 0 ? c.Issuer : c.Subject + " " + c.Issuer)
            .Any(text => names.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Combines the per-site results: any filter chain wins; reachable sites without it mean "not filtered".</summary>
    public static IspFilterResult Decide(IspFilterPolicy policy, IReadOnlyList<(string Host, List<ChainCert>? Chain, string? Error)> probes, DateTime now)
    {
        var reached = probes.Where(p => p.Chain is { Count: > 0 }).ToList();
        var hit = reached.FirstOrDefault(p => IsFilterChain(policy, p.Chain!));
        if (hit.Chain != null) return new(IspFilterStatus.Filtered, now, $"{hit.Host}: {Signer(hit.Chain)}", hit.Chain);
        if (reached.Count > 0)
        {
            var first = reached[0];
            return new(IspFilterStatus.NotFiltered, now, $"{first.Host}: {Signer(first.Chain!)}", first.Chain!);
        }
        return new(IspFilterStatus.NoNetwork, now, probes.Select(p => p.Error).FirstOrDefault(e => e != null) ?? "", []);
    }

    /// <summary>The top of the chain (the root, or the last certificate the server sent).</summary>
    public static string Signer(IReadOnlyList<ChainCert> chain) => chain.Count == 0 ? "" : ShortName(chain[^1].Subject == chain[^1].Issuer ? chain[^1].Subject : chain[^1].Issuer);

    public static string ShortName(string dn)
    {
        foreach (var part in dn.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) return p[3..];
        }
        return dn;
    }

    public static string Normalize(string thumbprint) => new(thumbprint.Where(char.IsAsciiHexDigit).Select(char.ToUpperInvariant).ToArray());
}
