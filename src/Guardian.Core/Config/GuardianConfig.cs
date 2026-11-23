using Guardian.Core.Scheduling;

namespace Guardian.Core.Config;

public sealed class GuardianConfig
{
    public int Version { get; set; } = 1;
    /// <summary>Configurable (random by default) Windows service name.</summary>
    public string ServiceName { get; set; } = "";
    public string AdminPasswordHash { get; set; } = "";
    /// <summary>Recovery ("rescue") password. Unlocks everything, including uninstall.</summary>
    public string RecoveryPasswordHash { get; set; } = "";
    public List<UserLockoutRule> UserLockouts { get; set; } = new();
    public AppPolicy Apps { get; set; } = new();
    public NetworkPolicy Network { get; set; } = new();
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
    /// <summary>Domains; "example.com" also covers subdomains.</summary>
    public List<string> Domains { get; set; } = new();
    /// <summary>Executables denied any network access (outbound firewall rules).</summary>
    public List<string> BlockedAppPaths { get; set; } = new();
}
