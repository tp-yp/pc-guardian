using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using Guardian.Core.Config;
using Microsoft.Win32;

namespace Guardian.Service.Enforcers;

/// <summary>
/// Keeps the machine pointed at the local DNS filter and maintains Windows Firewall rules:
///  * per-app outbound block rules (network.blockedAppPaths);
///  * block outbound DNS (53) and DNS-over-TLS (853) to anything but loopback, so apps can't bypass the filter;
///  * browser policies that turn off built-in DNS-over-HTTPS;
///  * Windows Firewall kept on for all profiles;
///  * with URL filtering: system proxy -> local proxy, and outbound "block by default" so only the
///    proxy, Windows services (svchost/System) and allowDirectApps can connect out directly.
///  * "internet only through the filter" not confirmed (isolated): outbound blocked by default for
///    services, a block rule for every program in a signed-in session, and only Guardian itself
///    (which keeps checking for the filter), DHCP and the filter's own service programs may connect.
/// Everything is re-checked every 30 seconds, so manual changes are reverted.
/// </summary>
public sealed class NetworkEnforcer(ILogger<NetworkEnforcer> log)
{
    public const string RuleGroup = "Guardian policy";
    private const string NonLoopbackV4 = "0.0.0.0-126.255.255.255,128.0.0.0-255.255.255.255";
    private const string NonLoopback = NonLoopbackV4 + ",::2-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";
    /// <summary>Firewall condition "processes of signed-in users" (the INTERACTIVE group); services are not in it.</summary>
    private const string InteractiveSddl = "O:LSD:(A;;CC;;;IU)";
    private DateTime _nextCheck = DateTime.MinValue;
    private string _rulesSig = "";
    private bool? _active; // null = unknown (first tick after start)

    /// <param name="isolated">The filtered-line switch is on and the filter is not confirmed: cut the internet.</param>
    public void Tick(GuardianConfig cfg, bool force = false, bool isolated = false)
    {
        if (!force && DateTime.UtcNow < _nextCheck) return;
        _nextCheck = DateTime.UtcNow.AddSeconds(30);
        if (!cfg.Network.Enabled)
        {
            if (_active != false) { Restore(); _rulesSig = ""; _active = false; }
            return;
        }
        _active = true;

        EnsureFirewallOn(cfg.Network.UrlFiltering || isolated);
        EnsureRules(cfg, isolated);
        if (cfg.Network.UrlFiltering) { if (!SystemProxySettings.IsApplied()) SystemProxySettings.Apply(); }
        else if (cfg.Vpn.Block) { if (!SystemProxySettings.IsDirectApplied()) SystemProxySettings.ApplyDirect(); }
        else if (SystemProxySettings.IsApplied() || SystemProxySettings.IsDirectApplied()) SystemProxySettings.Remove();
        EnsureLocalDns();
        EnsureBrowserDohOff();
    }

    private List<string> _accounts = [];
    private DateTime _accountsAt = DateTime.MinValue;

    private IReadOnlyCollection<string> Accounts()
    {
        if (DateTime.UtcNow - _accountsAt > TimeSpan.FromMinutes(5))
        {
            try { _accounts = AppCatalogScanner.LocalAccounts(); } catch (Exception ex) { log.LogDebug(ex, "account list failed"); }
            _accountsAt = DateTime.UtcNow;
        }
        return _accounts;
    }

    /// <summary>Firewall condition "only these local accounts" (SDDL), or null if a name cannot be resolved.</summary>
    private static string? UsersSddl(IEnumerable<string> users)
    {
        var aces = new List<string>();
        foreach (var u in users)
        {
            try { aces.Add($"(A;;CC;;;{((System.Security.Principal.SecurityIdentifier)new System.Security.Principal.NTAccount(u).Translate(typeof(System.Security.Principal.SecurityIdentifier))).Value})"); }
            catch (Exception) { /* account removed */ }
        }
        return aces.Count == 0 ? null : "O:LSD:" + string.Concat(aces.Distinct());
    }

    private void EnsureRules(GuardianConfig cfg, bool isolated)
    {
        var p = cfg.Network;
        var wanted = new List<(string Name, Action<dynamic> Setup)>
        {
            ("Guardian DNS UDP", r => { r.Protocol = 17; r.RemotePorts = "53,853"; r.RemoteAddresses = NonLoopbackV4; }),
            ("Guardian DNS TCP", r => { r.Protocol = 6; r.RemotePorts = "53,853"; r.RemoteAddresses = NonLoopbackV4; }),
        };
        // "No internet" programs: for everyone, or (with per-account rules) only for some accounts.
        foreach (var (path, users) in Guardian.Core.Policy.UserScopes.InternetBlocks(cfg, cfg.UserPolicies.Count > 0 ? Accounts() : []))
        {
            if (users is null) { wanted.Add(($"Guardian app {ShortHash(path)}", r => { r.ApplicationName = path; })); continue; }
            var sddl = UsersSddl(users);
            if (sddl is null) continue;
            wanted.Add(($"Guardian app {ShortHash(path + sddl)}", r => { r.ApplicationName = path; r.LocalUserAuthorizedList = sddl; }));
        }
        if (cfg.Vpn.Block)
        {
            // VPN protocols: IKEv2/IPsec (500, 4500), L2TP (1701), PPTP (1723 + GRE), OpenVPN (1194), WireGuard (51820).
            // SSTP uses 443 like any website and cannot be told apart here; the VPN programs themselves are blocked by name.
            wanted.Add(("Guardian VPN UDP", r => { r.Protocol = 17; r.RemotePorts = "500,4500,1701,1194,51820"; r.RemoteAddresses = NonLoopback; }));
            wanted.Add(("Guardian VPN TCP", r => { r.Protocol = 6; r.RemotePorts = "1723,1194"; r.RemoteAddresses = NonLoopback; }));
            wanted.Add(("Guardian VPN GRE", r => { r.Protocol = 47; r.RemoteAddresses = NonLoopback; }));
        }
        if (isolated)
        {
            // Programs people run: blocked outright (a block rule beats any allow rule Windows or an app added).
            wanted.Add(("Guardian isolate users", r => { r.RemoteAddresses = NonLoopback; r.LocalUserAuthorizedList = InteractiveSddl; }));
            // Services: blocked by default; only Guardian (to keep checking), DHCP and the filter's own programs.
            var svchost = Path.Combine(Environment.SystemDirectory, "svchost.exe");
            wanted.Add(("Guardian isolate DHCP", r => { r.ApplicationName = svchost; r.Protocol = 17; r.RemotePorts = "67,547"; r.Action = 1; }));
            var allow = new List<string> { Environment.ProcessPath! };
            allow.AddRange(cfg.IspFilter.ClientApps.Where(x => !string.IsNullOrWhiteSpace(x)));
            foreach (var app in allow.Distinct(StringComparer.OrdinalIgnoreCase))
                wanted.Add(($"Guardian isolate allow {ShortHash(app)}", r => { r.ApplicationName = app; r.Action = 1; }));
        }
        else if (p.UrlFiltering)
        {
            // Allow rules for "block outbound by default". Block rules above still win over these.
            var allow = new List<string> { Environment.ProcessPath!, Path.Combine(Environment.SystemDirectory, "svchost.exe"), "System" };
            allow.AddRange(p.AllowDirectApps.Where(x => !string.IsNullOrWhiteSpace(x)));
            foreach (var app in allow.Distinct(StringComparer.OrdinalIgnoreCase))
                wanted.Add(($"Guardian allow {ShortHash(app)}", r => { r.ApplicationName = app; r.Action = 1; }));
        }

        var fw = Fw();
        var existing = new HashSet<string>();
        foreach (dynamic r in fw.Rules) if (r.Grouping == RuleGroup) existing.Add((string)r.Name);
        var sig = string.Join("|", wanted.Select(w => w.Name));
        if (sig == _rulesSig && wanted.All(w => existing.Contains(w.Name))) return;

        foreach (var name in existing) fw.Rules.Remove(name);
        foreach (var (name, setup) in wanted)
        {
            dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
            rule.Name = name;
            rule.Grouping = RuleGroup;
            rule.Direction = 2;  // outbound
            rule.Action = 0;     // block
            rule.Profiles = 0x7FFFFFFF;
            rule.Enabled = true;
            try
            {
                setup(rule); // may override Action to allow
                fw.Rules.Add(rule);
            }
            catch (Exception ex) { log.LogWarning(ex, "Firewall rule {Name} could not be added", name); }
        }
        _rulesSig = sig;
        log.LogInformation("Firewall rules applied ({N})", wanted.Count);
    }

    private static void EnsureFirewallOn(bool blockOutboundByDefault)
    {
        var fw = Fw();
        var wantedAction = blockOutboundByDefault ? 0 : 1; // NET_FW_ACTION_BLOCK / ALLOW
        foreach (var profile in new[] { 1, 2, 4 }) // domain, private, public
        {
            if (!fw.FirewallEnabled[profile]) fw.FirewallEnabled[profile] = true;
            if ((int)fw.DefaultOutboundAction[profile] != wantedAction) fw.DefaultOutboundAction[profile] = wantedAction;
        }
    }

    private void EnsureLocalDns()
    {
        foreach (var nic in Adapters())
        {
            var dns = nic.GetIPProperties().DnsAddresses;
            if (!(dns.Count > 0 && dns.All(IPAddress.IsLoopback)))
            {
                Netsh($"interface ipv4 set dnsservers name=\"{nic.Name}\" source=static address=127.0.0.1 register=none validate=no");
                Netsh($"interface ipv6 set dnsservers name=\"{nic.Name}\" source=static address=::1 register=none validate=no");
                log.LogInformation("DNS of {Nic} pointed to the local filter", nic.Name);
            }
        }
    }

    private static readonly (string Key, string Name, object Value)[] BrowserPolicies =
    [
        (@"SOFTWARE\Policies\Google\Chrome", "DnsOverHttpsMode", "off"),
        (@"SOFTWARE\Policies\Microsoft\Edge", "DnsOverHttpsMode", "off"),
        (@"SOFTWARE\Policies\BraveSoftware\Brave", "DnsOverHttpsMode", "off"),
        (@"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", "Enabled", 0),
        (@"SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", "Locked", 1),
    ];

    private static void EnsureBrowserDohOff()
    {
        foreach (var (key, name, value) in BrowserPolicies)
        {
            using var k = Registry.LocalMachine.CreateSubKey(key);
            if (!Equals(k.GetValue(name), value)) k.SetValue(name, value);
        }
    }

    /// <summary>Undo everything (policy disabled or uninstall).</summary>
    public void Restore()
    {
        try { SystemProxySettings.Remove(); } catch (Exception ex) { log.LogWarning(ex, "Proxy settings cleanup failed"); }
        try
        {
            var fw = Fw();
            foreach (var profile in new[] { 1, 2, 4 }) fw.DefaultOutboundAction[profile] = 1;
            var mine = new List<string>();
            foreach (dynamic r in fw.Rules) if (r.Grouping == RuleGroup) mine.Add((string)r.Name);
            foreach (var n in mine) fw.Rules.Remove(n);
        }
        catch (Exception ex) { log.LogWarning(ex, "Firewall cleanup failed"); }

        foreach (var nic in Adapters())
            if (nic.GetIPProperties().DnsAddresses.Any(IPAddress.IsLoopback))
            {
                Netsh($"interface ipv4 set dnsservers name=\"{nic.Name}\" source=dhcp");
                Netsh($"interface ipv6 set dnsservers name=\"{nic.Name}\" source=dhcp");
            }

        foreach (var (key, name, _) in BrowserPolicies)
        {
            using var k = Registry.LocalMachine.OpenSubKey(key, writable: true);
            k?.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    private static IEnumerable<NetworkInterface> Adapters() =>
        NetworkInterface.GetAllNetworkInterfaces().Where(n =>
            n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel));

    private static dynamic Fw() => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;

    private static string ShortHash(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s.ToLowerInvariant())))[..10];

    private void Netsh(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("netsh.exe", args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.WaitForExit(10_000);
        if (p.ExitCode != 0) log.LogWarning("netsh {Args} failed: {Out}", args, p.StandardOutput.ReadToEnd().Trim());
    }
}
