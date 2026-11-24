using Guardian.Core.Policy;

namespace Guardian.Core.Network;

/// <summary>
/// Well-known VPN and proxy programs and websites, used by the "block VPN and proxies" switch.
/// Programs are recognised by file name or by the name written inside the file, so renaming does not help.
/// The list cannot be complete; full-URL filtering (which blocks anything that does not pass the filter)
/// and "block unknown programs" close most of what is not listed.
/// </summary>
public static class VpnLists
{
    public static readonly HashSet<string> Programs = new(StringComparer.OrdinalIgnoreCase)
    {
        "openvpn.exe", "openvpn-gui.exe", "openvpnconnect.exe", "openvpn connect.exe", "wireguard.exe", "wg.exe",
        "nordvpn.exe", "nordvpn-service.exe", "expressvpn.exe", "expressvpnd.exe", "protonvpn.exe", "protonvpn.client.exe", "protonvpnservice.exe",
        "surfshark.exe", "surfshark.service.exe", "windscribe.exe", "windscribeservice.exe", "tunnelbear.exe", "hotspotshield.exe", "hsscp.exe",
        "cyberghost.exe", "cyberghost.service.exe", "pia-client.exe", "pia-service.exe", "mullvad vpn.exe", "mullvad-daemon.exe",
        "ipvanish.exe", "vyprvpn.exe", "hide.me.exe", "hidemevpn.exe", "zenmate.exe", "betternet.exe", "urban vpn.exe", "urbanvpn.exe",
        "psiphon3.exe", "psiphon.exe", "ultrasurf.exe", "freegate.exe", "lantern.exe", "tor.exe", "torbrowser.exe", "hola.exe",
        "proxifier.exe", "shadowsocks.exe", "v2rayn.exe", "v2ray.exe", "xray.exe", "clash.exe", "clash for windows.exe", "outline-client.exe",
        "outline.exe", "softether vpn client manager.exe", "vpncmgr.exe", "vpnclient.exe", "planet vpn.exe", "x-vpn.exe",
        "cloudflare warp.exe", "warp-svc.exe", "touchvpn.exe", "vpnunlimited.exe", "browsec.exe", "hideman.exe",
    };

    public static readonly string[] Domains =
    [
        "nordvpn.com", "expressvpn.com", "protonvpn.com", "surfshark.com", "windscribe.com", "tunnelbear.com",
        "hotspotshield.com", "cyberghostvpn.com", "privateinternetaccess.com", "mullvad.net", "ipvanish.com", "vyprvpn.com", "hide.me",
        "zenmate.com", "betternet.co", "urban-vpn.com", "psiphon.ca", "psiphon3.com", "ultrasurf.us", "getlantern.org", "torproject.org",
        "hola.org", "openvpn.net", "wireguard.com", "planetvpn.com", "xvpn.io", "touchvpn.net", "vpnunlimited.com", "browsec.com",
        "hidester.com", "kproxy.com", "croxyproxy.com", "proxysite.com", "hide.mn", "4everproxy.com", "proxyium.com", "blockaway.net",
        "croxyproxy.rocks", "proxfree.com", "whoer.net",
    ];

    public static bool IsVpnProgram(string path, string? originalName)
    {
        var file = path.Replace('/', '\\');
        file = file[(file.LastIndexOf('\\') + 1)..];
        return Programs.Contains(file) || (originalName is { Length: > 0 } o && Programs.Contains(AppPolicyEvaluator.ExeName(o)));
    }

    public static bool IsVpnSite(string host) =>
        Domains.Any(d => UrlPolicy.HostMatches(host, d));
}
