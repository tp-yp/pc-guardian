using Guardian.Core.Config;
using Guardian.Core.Policy;

namespace Guardian.Core.Network;

public static class DnsFilter
{
    /// <summary>Public DNS-over-HTTPS / DoT hosts. Blocked so apps cannot resolve around the filter.
    /// "use-application-dns.net" is Firefox's canary: answering it disables Firefox's automatic DoH.</summary>
    public static readonly string[] AlwaysBlocked =
    [
        "use-application-dns.net", "dns.google", "dns.google.com", "cloudflare-dns.com", "one.one.one.one",
        "dns.quad9.net", "doh.opendns.com", "dns.adguard.com", "dns.adguard-dns.com", "doh.cleanbrowsing.org",
        "dns.nextdns.io", "doh.mullvad.net", "dns.mullvad.net", "chrome.cloudflare-dns.com", "mozilla.cloudflare-dns.com",
    ];

    /// <summary>Hosts Windows needs to function; allowed even in allowlist mode.</summary>
    public static readonly string[] AlwaysAllowed =
    [
        "msftconnecttest.com", "msftncsi.com", "windowsupdate.com", "update.microsoft.com", "delivery.mp.microsoft.com",
        "time.windows.com", "login.live.com", "login.microsoftonline.com", "ctldl.windowsupdate.com", "digicert.com",
        "ocsp.digicert.com", "crl.microsoft.com", "lencr.org",
    ];

    public static bool IsAllowed(NetworkPolicy policy, string host)
    {
        if (!policy.Enabled) return true;
        if (AlwaysBlocked.Any(d => UrlPolicy.HostMatches(host, d))) return false;
        if (policy.Mode == UrlMode.Allowlist && AlwaysAllowed.Any(d => UrlPolicy.HostMatches(host, d))) return true;
        return UrlPolicy.IsHostAllowed(policy, host);
    }
}
