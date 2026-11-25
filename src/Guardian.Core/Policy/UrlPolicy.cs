using Guardian.Core.Config;

namespace Guardian.Core.Policy;

/// <summary>
/// List entries are either a domain ("example.com", covers subdomains) or a URL prefix
/// ("example.com/watch", "https://www.youtube.com/shorts"). Path prefixes need the HTTPS proxy
/// to be seen; DNS-level filtering only ever acts on whole domains.
/// </summary>
public static class UrlPolicy
{
    public sealed record Entry(string Host, string Path);

    public static Entry ParseEntry(string raw)
    {
        var s = raw.Trim();
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        var slash = s.IndexOf('/');
        var host = slash < 0 ? s : s[..slash];
        var path = slash < 0 ? "" : s[slash..];
        if (path == "/") path = "";
        var colon = host.LastIndexOf(':');
        if (colon > 0 && !host.Contains(']')) host = host[..colon];
        return new(host.TrimStart('*', '.').TrimEnd('.').ToLowerInvariant(), path);
    }

    /// <summary>Extracts the host from a URL or bare domain, lower-cased, without port or trailing dot.</summary>
    public static string NormalizeHost(string input) => ParseEntry(input).Host;

    /// <summary>True when host equals the domain or is a subdomain of it.</summary>
    public static bool HostMatches(string host, string domain)
    {
        host = NormalizeHost(host);
        domain = NormalizeHost(domain);
        return domain.Length > 0 && (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal));
    }

    public static bool UrlMatches(string entry, string host, string path)
    {
        var e = ParseEntry(entry);
        if (!HostMatches(host, e.Host)) return false;
        if (e.Path.Length == 0) return true;
        return (path.Length == 0 ? "/" : path).StartsWith(e.Path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Full decision for a request whose path is known (HTTP, or HTTPS through the proxy).</summary>
    public static bool IsUrlAllowed(NetworkPolicy policy, string host, string path)
    {
        if (!policy.Enabled || Network.LocalNetwork.Contains(policy.LocalAddresses, host)) return true;
        var blocklist = policy.Mode == UrlMode.Blocklist;
        if (policy.Exceptions.Any(d => UrlMatches(d, host, path))) return blocklist;
        var listed = policy.Domains.Any(d => UrlMatches(d, host, path));
        return blocklist ? !listed : listed;
    }

    /// <summary>
    /// Decision when only the host is known (DNS, TLS tunnels that are not decrypted).
    /// Blocklist: blocked only by whole-domain entries. Allowlist: allowed if any entry is on that host.
    /// </summary>
    public static bool IsHostAllowed(NetworkPolicy policy, string host)
    {
        if (!policy.Enabled || Network.LocalNetwork.Contains(policy.LocalAddresses, host)) return true;
        bool WholeHost(string d) => ParseEntry(d) is { Path: "" } e && HostMatches(host, e.Host);
        if (policy.Mode == UrlMode.Blocklist)
        {
            // A blocked domain still resolves if some page on it is an exception (the proxy filters the rest).
            if (policy.Exceptions.Any(d => HostMatches(host, ParseEntry(d).Host))) return true;
            return !policy.Domains.Any(WholeHost);
        }
        if (policy.Exceptions.Any(WholeHost)) return false;
        return policy.Domains.Any(d => HostMatches(host, ParseEntry(d).Host));
    }

    /// <summary>Accepts "host", "host/path" or a full URL.</summary>
    public static bool IsAllowed(NetworkPolicy policy, string hostOrUrl)
    {
        var e = ParseEntry(hostOrUrl);
        return IsUrlAllowed(policy, e.Host, e.Path);
    }
}
