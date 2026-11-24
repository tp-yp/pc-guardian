using Guardian.Core.Scheduling;

namespace Guardian.Core.Config;

public sealed class GuardianConfig
{
    public int Version { get; set; } = 1;
    /// <summary>Configurable (random by default) Windows service name.</summary>
    public string ServiceName { get; set; } = "";
    /// <summary>Companion service that restarts the main one (and vice versa).</summary>
    public string WatchdogServiceName { get; set; } = "";
    public string AdminPasswordHash { get; set; } = "";
    /// <summary>Recovery ("rescue") password. Unlocks everything, including uninstall.</summary>
    public string RecoveryPasswordHash { get; set; } = "";
    public List<UserLockoutRule> UserLockouts { get; set; } = new();
    /// <summary>The management window locks itself after this many minutes without activity.</summary>
    public int UiIdleLockMinutes { get; set; } = 5;
    public static readonly int[] IdleLockChoices = [1, 3, 5, 10, 15, 30];
    /// <summary>General rules: apply to every Windows account that has no settings of its own.</summary>
    public AppPolicy Apps { get; set; } = new();
    public NetworkPolicy Network { get; set; } = new();
    /// <summary>Accounts with their own program and website rules (see <see cref="Policy.UserScopes"/>).</summary>
    public List<UserPolicy> UserPolicies { get; set; } = new();
    /// <summary>Hardening of the boot path (Safe Mode, recovery environment). Off by default.</summary>
    public BootPolicy Boot { get; set; } = new();
    /// <summary>Require the internet connection to go through an ISP content filter (NetFree, Etrog/NetSpark).</summary>
    public IspFilterPolicy IspFilter { get; set; } = new();
    public ClockPolicy Clock { get; set; } = new();
    public VpnPolicy Vpn { get; set; } = new();
    public WifiPolicy Wifi { get; set; } = new();

    /// <summary>
    /// Config for a fresh install: protected and running, but every blocking feature OFF,
    /// so nothing changes on the computer until the owner turns features on one by one.
    /// </summary>
    public static GuardianConfig CreateInitial(string serviceName, string watchdogName, string adminHash, string recoveryHash) => new()
    {
        ServiceName = serviceName,
        WatchdogServiceName = watchdogName,
        AdminPasswordHash = adminHash,
        RecoveryPasswordHash = recoveryHash,
        Apps = new AppPolicy { Enabled = false },
        Network = new NetworkPolicy { Enabled = false, UrlFiltering = false },
    };
}

/// <summary>
/// Separate rules for one Windows account. Only the per-user parts are used: which programs are
/// locked / allowed, "block unknown programs", the website list (mode, entries, exceptions) and
/// which programs have no internet. Feature switches, the filter, no-decrypt and direct-access
/// lists always come from the general settings.
/// </summary>
public sealed class UserPolicy
{
    public string UserName { get; set; } = "";
    public AppPolicy Apps { get; set; } = new();
    public NetworkPolicy Network { get; set; } = new();
}

/// <summary>
/// "Boot lockdown": keeps the recovery environment (WinRE) disabled, removes any request to restart
/// into Safe Mode, turns off automatic repair and the F8 menu. Only turned on after the owner
/// explicitly confirms; everything is put back when it is turned off or Guardian is removed.
/// Booting from an external disk is not blocked (that needs a BIOS/UEFI password).
/// </summary>
public sealed class BootPolicy
{
    public bool Lockdown { get; set; }
}

/// <summary>
/// "Internet only through the filter": filters such as NetFree and Etrog (NetSpark) re-sign HTTPS with
/// their own root certificate. Guardian regularly opens TLS connections to well-known sites and checks
/// who signed them. If it is not the filter, all internet traffic is blocked until the filter is back.
/// </summary>
/// <summary>Blocks VPN programs, VPN connections and proxies, which would get around the filtering.</summary>
/// <summary>Wi-Fi: only networks already known to this computer may be joined (Windows' own network filters).</summary>
public sealed class WifiPolicy
{
    public bool KnownOnly { get; set; }
    /// <summary>Network names (SSIDs) that stay allowed; filled from the saved Wi-Fi networks when the switch is turned on.</summary>
    public List<string> Allowed { get; set; } = new();
}

public sealed class VpnPolicy
{
    public bool Block { get; set; }
}

/// <summary>Keeps the computer's clock honest so schedules cannot be dodged by changing the time.</summary>
public sealed class ClockPolicy
{
    public bool Locked { get; set; }
    /// <summary>The time zone to keep (Windows id), saved when the lock is turned on.</summary>
    public string TimeZoneId { get; set; } = "";
}

public sealed class IspFilterPolicy
{
    public bool Required { get; set; }
    /// <summary>Which filtering providers count: "NetFree", "Etrog" (Etrog is built on NetSpark), or both.</summary>
    public List<string> Providers { get; set; } = ["NetFree", "Etrog"];
    /// <summary>Extra text that identifies the filter's certificate (matched in subject/issuer names, case-insensitive).
    /// Provider names are not listed here; they come from <see cref="Providers"/>.</summary>
    public List<string> IssuerNames { get; set; } = new();
    /// <summary>Exact certificates trusted as the filter (SHA-1 thumbprints, e.g. pinned with "this is my filter").</summary>
    public List<string> Thumbprints { get; set; } = new();
    /// <summary>Programs that still may connect while blocked (e.g. a filter's own connection client).</summary>
    public List<string> ClientApps { get; set; } = new();
}

/// <summary>While the schedule is active the user is signed out and cannot sign in.</summary>
public sealed class UserLockoutRule
{
    public string UserName { get; set; } = "";
    public WeeklySchedule LockedDuring { get; set; } = new();
    public int WarningMinutes { get; set; } = 5;
    /// <summary>"Addiction prevention": sign the user out after this many minutes of continuous use (0 = off). Signing in again is allowed.</summary>
    public int BreakEveryMinutes { get; set; }
}

public enum RuleAction { Allow, Block }

public sealed class AppRule
{
    public RuleAction Action { get; set; }
    /// <summary>Full path or folder prefix, e.g. C:\Program Files\Mozilla Firefox\</summary>
    public string? PathPrefix { get; set; }
    /// <summary>SHA-256 of the executable (hex). Survives renaming/moving portable apps.</summary>
    public string? Sha256 { get; set; }
    /// <summary>Executable file name, e.g. "chrome.exe". Block rules also match the name written inside the
    /// file (its original file name), so renaming the file does not get around them.</summary>
    public string? FileName { get; set; }
    /// <summary>The original file name stored inside the program (version info), e.g. "Discord.exe".</summary>
    public string? OriginalName { get; set; }
    /// <summary>When the rule applies. Empty = always.</summary>
    public WeeklySchedule When { get; set; } = new();
}

public sealed class AppPolicy
{
    public bool Enabled { get; set; } = true;
    /// <summary>Unknown executables are blocked unless an Allow rule matches.</summary>
    public bool DefaultDeny { get; set; } = true;
    /// <summary>Treat everything under Program Files as known (only admins can install there).
    /// Portable apps in Downloads, Desktop, USB drives etc. stay blocked by default.</summary>
    public bool TrustProgramFiles { get; set; } = true;
    public List<AppRule> Rules { get; set; } = new();
}

public enum UrlMode { Blocklist, Allowlist }

public sealed class NetworkPolicy
{
    public bool Enabled { get; set; } = true;
    public UrlMode Mode { get; set; } = UrlMode.Blocklist;
    /// <summary>Domains ("example.com", covers subdomains) or URL prefixes ("youtube.com/shorts").</summary>
    public List<string> Domains { get; set; } = new();
    /// <summary>Exceptions to <see cref="Domains"/> with the opposite meaning: in blocklist mode always
    /// allowed, in allowlist mode always blocked. Lets the owner allow one page of a blocked site, etc.</summary>
    public List<string> Exceptions { get; set; } = new();
    /// <summary>Executables denied any network access (outbound firewall rules).</summary>
    public List<string> BlockedAppPaths { get; set; } = new();
    /// <summary>
    /// Route web traffic through the local HTTPS-inspecting proxy so full URLs can be filtered.
    /// Also switches Windows Firewall to "block outbound by default": only the proxy, Windows
    /// services and <see cref="AllowDirectApps"/> may connect out directly.
    /// </summary>
    public bool UrlFiltering { get; set; } = true;
    /// <summary>Hosts tunnelled without decryption (banks, apps with certificate pinning). Filtered by domain only.</summary>
    public List<string> NoDecryptDomains { get; set; } = new();
    /// <summary>Executables allowed to reach the internet directly, bypassing the proxy (e.g. games, Steam).</summary>
    public List<string> AllowDirectApps { get; set; } = new();
}
