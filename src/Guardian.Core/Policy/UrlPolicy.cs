using Guardian.Core.Config;

namespace Guardian.Core.Policy;

public static class UrlPolicy
{
    /// <summary>Extracts the host from a URL or bare domain, lower-cased, without port or trailing dot.</summary>
    public static string NormalizeHost(string input)
    {
        var s = input.Trim();
        if (Uri.TryCreate(s.Contains("://") ? s : "http://" + s, UriKind.Absolute, out var uri)) s = uri.Host;
        return s.TrimEnd('.').ToLowerInvariant();
    }

    /// <summary>True when host equals the domain or is a subdomain of it.</summary>
    public static bool HostMatches(string host, string domain)
    {
        host = NormalizeHost(host);
        domain = NormalizeHost(domain.TrimStart('*', '.'));
        return host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);
    }

    public static bool IsAllowed(NetworkPolicy policy, string hostOrUrl)
    {
        if (!policy.Enabled) return true;
        var listed = policy.Domains.Any(d => HostMatches(hostOrUrl, d));
        return policy.Mode == UrlMode.Blocklist ? !listed : listed;
    }
}
