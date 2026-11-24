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

/// <summary>While the schedule is active the user is signed out and cannot sign in.</summary>
public sealed class UserLockoutRule
{
    public string UserName { get; set; } = "";
    public WeeklySchedule LockedDuring { get; set; } = new();
    public int WarningMinutes { get; set; } = 5;
}

public enum RuleAction { Allow, Block }

public sealed class AppRule
{
    public RuleAction Action { get; set; }
    /// <summary>Full path or folder prefix, e.g. C:\Program Files\Mozilla Firefox\</summary>
    public string? PathPrefix { get; set; }
    /// <summary>SHA-256 of the executable (hex). Survives renaming/moving portable apps.</summary>
    public string? Sha256 { get; set; }
    /// <summary>Executable file name, e.g. "chrome.exe" (weakest match; easy to rename).</summary>
    public string? FileName { get; set; }
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
