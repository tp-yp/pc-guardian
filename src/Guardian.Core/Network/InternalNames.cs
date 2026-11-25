namespace Guardian.Core.Network;

/// <summary>
/// Names that only the local network's own DNS server knows: a school or office server
/// ("dbserver", "files.college.local"), a printer ("printer.lan") or the reverse lookup of a home-network
/// address. Public DNS cannot answer these, so the local filter asks the network's DNS server instead.
/// </summary>
public static class InternalNames
{
    /// <summary>Endings that are never sites on the internet.</summary>
    private static readonly string[] PrivateEndings =
        ["local", "lan", "home", "internal", "intranet", "corp", "private", "localdomain", "home.arpa"];

    /// <summary>
    /// True for names that can never be a site on the internet (one-word names, private endings,
    /// reverse lookups of home-network addresses). These skip the site filter.
    /// </summary>
    public static bool IsPrivateOnly(string name)
    {
        var n = Normalize(name);
        if (n.Length == 0) return false;
        if (!n.Contains('.')) return true;
        if (PrivateEndings.Any(e => n == e || n.EndsWith("." + e, StringComparison.Ordinal))) return true;
        return IsPrivateReverse(n);
    }

    /// <summary>
    /// True when the network's own DNS server should answer: private-only names, and names under the
    /// network's DNS suffix ("college.ac.il"). Suffix names still go through the site filter first.
    /// One-word suffixes ("com") are ignored, so a network cannot take over a whole public ending.
    /// </summary>
    public static bool IsInternal(string name, IEnumerable<string> networkSuffixes)
    {
        if (IsPrivateOnly(name)) return true;
        var n = Normalize(name);
        foreach (var raw in networkSuffixes)
        {
            var s = Normalize(raw);
            if (!s.Contains('.')) continue;
            if (n == s || n.EndsWith("." + s, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool IsPrivateReverse(string n)
    {
        const string v4 = ".in-addr.arpa";
        if (!n.EndsWith(v4, StringComparison.Ordinal)) return false;
        var parts = n[..^v4.Length].Split('.').Reverse().ToArray(); // first octet first
        if (parts.Length == 0 || !int.TryParse(parts[0], out var a)) return false;
        int.TryParse(parts.Length > 1 ? parts[1] : "-1", out var b);
        if (parts.Length == 1) return a == 10; // "10.in-addr.arpa"
        return a == 10 || (a == 192 && b == 168) || (a == 172 && b is >= 16 and <= 31) || (a == 169 && b == 254);
    }

    private static string Normalize(string name) => (name ?? "").Trim().TrimEnd('.').ToLowerInvariant();
}
