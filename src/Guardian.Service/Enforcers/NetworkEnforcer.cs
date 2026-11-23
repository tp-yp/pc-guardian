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
/// Everything is re-checked every 30 seconds, so manual changes are reverted.
/// </summary>
public sealed class NetworkEnforcer(ILogger<NetworkEnforcer> log)
{
    public const string RuleGroup = "Guardian policy";
    private const string NonLoopbackV4 = "0.0.0.0-126.255.255.255,128.0.0.0-255.255.255.255";
    private DateTime _nextCheck = DateTime.MinValue;
    private string _rulesSig = "";

    public void Tick(GuardianConfig cfg, bool force = false)
    {
        if (!force && DateTime.UtcNow < _nextCheck) return;
        _nextCheck = DateTime.UtcNow.AddSeconds(30);
        if (!cfg.Network.Enabled) { Restore(); _rulesSig = ""; return; }

        EnsureFirewallOn(cfg.Network.UrlFiltering);
        EnsureRules(cfg.Network);
        if (cfg.Network.UrlFiltering) { if (!SystemProxySettings.IsApplied()) SystemProxySettings.Apply(); }
        else if (SystemProxySettings.IsApplied()) SystemProxySettings.Remove();
        EnsureLocalDns();
        EnsureBrowserDohOff();
    }

    private void EnsureRules(NetworkPolicy p)
    {
        var wanted = new List<(string Name, Action<dynamic> Setup)>
        {
            ("Guardian DNS UDP", r => { r.Protocol = 17; r.RemotePorts = "53,853"; r.RemoteAddresses = NonLoopbackV4; }),
            ("Guardian DNS TCP", r => { r.Protocol = 6; r.RemotePorts = "53,853"; r.RemoteAddresses = NonLoopbackV4; }),
        };
        foreach (var path in p.BlockedAppPaths.Where(x => !string.IsNullOrWhiteSpace(x)))
            wanted.Add(($"Guardian app {ShortHash(path)}", r => { r.ApplicationName = path; }));
        if (p.UrlFiltering)
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
            setup(rule); // may override Action to allow
            fw.Rules.Add(rule);
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
