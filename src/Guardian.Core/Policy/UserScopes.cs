using System.Text.Json;
using Guardian.Core.Config;
using Guardian.Core.Lockout;
using Guardian.Core.Network;
using Guardian.Core.Storage;

namespace Guardian.Core.Policy;

/// <summary>
/// General rules plus optional per-account rules. A user without settings of their own follows the
/// general rules. Program rules (including the program switches) and website lists are per account.
/// What works for the whole computer stays shared: the network layer and full-URL filtering switches,
/// the no-decrypt / direct-access lists, boot lockdown, the filtered-line check and the clock lock.
/// </summary>
public static class UserScopes
{
    public static UserPolicy? Find(GuardianConfig cfg, string? user) =>
        string.IsNullOrWhiteSpace(user) ? null : cfg.UserPolicies.FirstOrDefault(p => LockoutPlanner.SameUser(p.UserName, user));

    /// <summary>The rules that apply to programs and web requests of this account (null/unknown = general).</summary>
    public static GuardianConfig Effective(GuardianConfig cfg, string? user)
    {
        var p = Find(cfg, user);
        if (p is null) return cfg;
        return new GuardianConfig
        {
            ServiceName = cfg.ServiceName, WatchdogServiceName = cfg.WatchdogServiceName,
            UserLockouts = cfg.UserLockouts, UiIdleLockMinutes = cfg.UiIdleLockMinutes, ActivityHours = cfg.ActivityHours, Boot = cfg.Boot, IspFilter = cfg.IspFilter, Clock = cfg.Clock, Vpn = cfg.Vpn, Wifi = cfg.Wifi, Browsers = cfg.Browsers,
            Apps = Merge(cfg.Apps, p.Apps),
            Network = Merge(cfg.Network, p.Network),
        };
    }

    private static AppPolicy Merge(AppPolicy g, AppPolicy u) => u;

    private static NetworkPolicy Merge(NetworkPolicy g, NetworkPolicy u) => new()
    {
        Enabled = g.Enabled, UrlFiltering = g.UrlFiltering, NoDecryptDomains = g.NoDecryptDomains, AllowDirectApps = g.AllowDirectApps, LocalAddresses = g.LocalAddresses,
        Mode = u.Mode, Domains = u.Domains, Exceptions = u.Exceptions, BlockedAppPaths = u.BlockedAppPaths, AppSites = u.AppSites, TimedSites = u.TimedSites,
    };

    /// <summary>
    /// A config to edit for one scope with the usual editing functions. For the general scope it is
    /// <paramref name="cfg"/> itself; for a user it reads and writes that user's lists, while shared
    /// switches and lists still read and write the general ones. Call <see cref="Commit"/> afterwards.
    /// </summary>
    public static GuardianConfig View(GuardianConfig cfg, string? user)
    {
        var p = Find(cfg, user);
        if (p is null) return cfg;
        p.Network.Enabled = cfg.Network.Enabled;
        p.Network.UrlFiltering = cfg.Network.UrlFiltering;
        p.Network.NoDecryptDomains = cfg.Network.NoDecryptDomains;
        p.Network.AllowDirectApps = cfg.Network.AllowDirectApps;
        p.Network.LocalAddresses = cfg.Network.LocalAddresses;
        return new GuardianConfig { Apps = p.Apps, Network = p.Network, UserLockouts = cfg.UserLockouts, UiIdleLockMinutes = cfg.UiIdleLockMinutes, Boot = cfg.Boot, IspFilter = cfg.IspFilter, Clock = cfg.Clock, Vpn = cfg.Vpn, Wifi = cfg.Wifi, Browsers = cfg.Browsers };
    }

    /// <summary>Copies shared switches changed through a user view back to the general settings.</summary>
    public static void Commit(GuardianConfig cfg, GuardianConfig view)
    {
        if (ReferenceEquals(cfg, view)) return;
        cfg.Network.Enabled = view.Network.Enabled;
        cfg.Network.UrlFiltering = view.Network.UrlFiltering;
        // Shared lists were edited in place (same list objects); keep the saved user entry lean.
        view.Network.NoDecryptDomains = [];
        view.Network.AllowDirectApps = [];
        view.Network.LocalAddresses = [];
    }

    /// <summary>Gives an account settings of its own, starting as a copy of the general rules.</summary>
    public static bool TrySetOwnRules(GuardianConfig cfg, string user, bool own, out string error)
    {
        error = "";
        user = user.Trim();
        if (user.Length is 0 or > 104 || user.Any(ch => char.IsControl(ch) || "\\/\"[]:|<>+=;,?*@".Contains(ch)))
        { error = "Invalid user name."; return false; }
        var existing = Find(cfg, user);
        if (!own) { if (existing != null) cfg.UserPolicies.Remove(existing); return true; }
        if (existing != null) return true;
        cfg.UserPolicies.Add(new UserPolicy
        {
            UserName = user,
            Apps = new AppPolicy { Enabled = cfg.Apps.Enabled, TrustProgramFiles = cfg.Apps.TrustProgramFiles, BlockAdminTools = cfg.Apps.BlockAdminTools, BlockVideoFiles = cfg.Apps.BlockVideoFiles, DefaultDeny = cfg.Apps.DefaultDeny, Rules = Copy(cfg.Apps.Rules) },
            Network = new NetworkPolicy
            {
                Mode = cfg.Network.Mode, Domains = [.. cfg.Network.Domains], Exceptions = [.. cfg.Network.Exceptions],
                OtherDomains = [.. cfg.Network.OtherDomains], OtherExceptions = [.. cfg.Network.OtherExceptions],
                AppSites = cfg.Network.AppSites.Select(a => new AppSites { ExePath = a.ExePath, Sites = [.. a.Sites] }).ToList(),
                BlockedAppPaths = [.. cfg.Network.BlockedAppPaths], NoDecryptDomains = [], AllowDirectApps = [],
                TimedSites = cfg.Network.TimedSites.Select(CopyTimed).ToList(),
            },
        });
        return true;
    }

    private static TimedSite CopyTimed(TimedSite t) => new()
    {
        Site = t.Site, UseMinutes = t.UseMinutes, BlockMinutes = t.BlockMinutes,
        BlockedDuring = new() { Windows = [.. t.BlockedDuring.Windows] },
    };

    /// <summary>True if program blocking is on for anyone (the general rules or any account).</summary>
    /// <summary>"Program locking" is on for the whole computer or for at least one account.</summary>
    public static bool AnyProgramLocking(GuardianConfig cfg) => cfg.Apps.Enabled || cfg.UserPolicies.Any(p => p.Apps.Enabled);

    public static bool AnyAppBlocking(GuardianConfig cfg) => cfg.Apps.Enabled || cfg.Apps.BlockAdminTools || cfg.Apps.BlockVideoFiles || cfg.Vpn.Block ||
        cfg.UserPolicies.Any(p => p.Apps.Enabled || p.Apps.BlockAdminTools || p.Apps.BlockVideoFiles);

    /// <summary>What is on for one scope, for the dashboard.</summary>
    public static ScopeSummary Summarize(GuardianConfig cfg, string? user)
    {
        var e = Effective(cfg, user);
        var simple = e.Apps.Rules.Where(r => r.When.Windows.Count == 0).ToList();
        return new ScopeSummary(
            AppsEnabled: e.Apps.Enabled, DefaultDeny: e.Apps.Enabled && e.Apps.DefaultDeny,
            LockedApps: simple.Count(r => r.Action == RuleAction.Block),
            NoInternetApps: e.Network.BlockedAppPaths.Count,
            SitesOn: e.Network.Enabled, Allowlist: e.Network.Mode == UrlMode.Allowlist,
            Sites: e.Network.Domains.Count, Exceptions: e.Network.Exceptions.Count,
            Schedule: cfg.UserLockouts.Any(l => user != null && LockoutPlanner.SameUser(l.UserName, user) && l.LockedDuring.Windows.Count > 0),
            BreakEvery: cfg.UserLockouts.FirstOrDefault(l => user != null && LockoutPlanner.SameUser(l.UserName, user))?.BreakEveryMinutes ?? 0,
            AdminTools: e.Apps.BlockAdminTools,
            TimedApps: e.Apps.Rules.Count(r => r.Action == RuleAction.Block && r.When.Windows.Count > 0));
    }

    private static List<AppRule> Copy(List<AppRule> rules) =>
        JsonSerializer.Deserialize<List<AppRule>>(JsonSerializer.Serialize(rules, EncryptedConfigStore.Json), EncryptedConfigStore.Json)!;

    /// <summary>
    /// DNS cannot tell which account asked, so a name is only refused there when it is blocked for
    /// everybody (general rules and every account with its own rules). Per-account differences are
    /// enforced by the full-URL filter, which knows the program (and so the account) behind each request.
    /// </summary>
    public static bool DnsAllows(GuardianConfig cfg, string host) =>
        !(cfg.Vpn.Block && VpnLists.IsVpnSite(host)) && (DnsFilter.IsAllowed(cfg.Network, host) ||
        cfg.UserPolicies.Any(p => DnsFilter.IsAllowed(Effective(cfg, p.UserName).Network, host)));

    /// <summary>
    /// Firewall "no internet" rules. Returns each program with the accounts it is blocked for,
    /// or null when it is blocked for everyone (no per-account rules involved).
    /// </summary>
    public static List<(string Path, List<string>? Users)> InternetBlocks(GuardianConfig cfg, IReadOnlyCollection<string> accounts)
    {
        var result = new List<(string, List<string>?)>();
        var cmp = StringComparer.OrdinalIgnoreCase;
        if (cfg.UserPolicies.Count == 0)
        {
            foreach (var path in cfg.Network.BlockedAppPaths.Where(x => !string.IsNullOrWhiteSpace(x) && !ProtectedApps.IsProtected(x)).Distinct(cmp)) result.Add((path, null));
            return result;
        }
        var general = accounts.Where(a => Find(cfg, a) is null).ToList();
        var all = cfg.Network.BlockedAppPaths.Concat(cfg.UserPolicies.SelectMany(p => p.Network.BlockedAppPaths))
            .Where(x => !string.IsNullOrWhiteSpace(x) && !ProtectedApps.IsProtected(x)).Distinct(cmp);
        foreach (var path in all)
        {
            var users = new List<string>();
            if (cfg.Network.BlockedAppPaths.Contains(path, cmp)) users.AddRange(general);
            users.AddRange(cfg.UserPolicies.Where(p => p.Network.BlockedAppPaths.Contains(path, cmp)).Select(p => p.UserName));
            if (users.Count == 0) continue;
            var everyone = accounts.Count > 0 && accounts.All(a => users.Any(u => LockoutPlanner.SameUser(u, a)));
            result.Add((path, everyone ? null : users.Distinct(cmp).ToList()));
        }
        return result;
    }

    /// <summary>
    /// The rules for one connection: a program with a site list of its own may reach only those sites
    /// (and the home-network addresses); everything else follows the account's rules.
    /// </summary>
    public static NetworkPolicy ForProgram(NetworkPolicy account, string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return account;
        var rule = account.AppSites.FirstOrDefault(a => string.Equals(a.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
        if (rule is null) return account;
        return new NetworkPolicy
        {
            Enabled = true, UrlFiltering = account.UrlFiltering, Mode = UrlMode.Allowlist, Domains = rule.Sites,
            NoDecryptDomains = account.NoDecryptDomains, LocalAddresses = account.LocalAddresses,
        };
    }

    /// <summary>True if any rule anywhere matches by file hash (so hashing is needed).</summary>
    public static bool AnyHashRules(GuardianConfig cfg) =>
        cfg.Apps.Rules.Concat(cfg.UserPolicies.SelectMany(p => p.Apps.Rules)).Any(r => !string.IsNullOrEmpty(r.Sha256));
}

/// <summary>One dashboard panel: which protections are on for an account (or the general rules).</summary>
public sealed record ScopeSummary(bool AppsEnabled, bool DefaultDeny, int LockedApps, int NoInternetApps,
    bool SitesOn, bool Allowlist, int Sites, int Exceptions, bool Schedule, int BreakEvery = 0, int TimedApps = 0, bool AdminTools = false);
