using System.Text.Json;
using Guardian.Core.Security;
using Guardian.Core.Storage;

namespace Guardian.Core.Config;

/// <summary>Export/import of the editable policy. Secrets and identity never leave or enter through this path.</summary>
public static class ConfigEditing
{
    /// <summary>JSON of the config with password hashes removed.</summary>
    public static string Export(GuardianConfig cfg)
    {
        var copy = Clone(cfg);
        copy.AdminPasswordHash = "";
        copy.RecoveryPasswordHash = "";
        copy.ReleasedSnapshot = null;
        copy.Cooling.Pending = [];
        copy.Cooling.UninstallDueUtc = null;
        return JsonSerializer.Serialize(copy, EncryptedConfigStore.Json);
    }

    /// <summary>Replaces the policy parts of <paramref name="current"/> with those from <paramref name="json"/> (a backup).
    /// Passwords, service names, waiting changes and a pending removal are kept from the current config.</summary>
    public static void Import(GuardianConfig current, string json)
    {
        GuardianConfig? incoming;
        try { incoming = JsonSerializer.Deserialize<GuardianConfig>(json, EncryptedConfigStore.Json); }
        catch (JsonException ex) { throw new InvalidDataException("Not a Geder settings file: " + ex.Message); }
        if (incoming is null) throw new InvalidDataException("Empty config.");
        Validate(incoming);
        current.UserLockouts = incoming.UserLockouts;
        current.UiIdleLockMinutes = GuardianConfig.IdleLockChoices.Contains(incoming.UiIdleLockMinutes) ? incoming.UiIdleLockMinutes : current.UiIdleLockMinutes;
        current.Apps = incoming.Apps;
        current.Network = incoming.Network;
        current.UserPolicies = incoming.UserPolicies;
        current.Boot = incoming.Boot;
        current.IspFilter = incoming.IspFilter;
        current.Clock = incoming.Clock;
        current.Vpn = incoming.Vpn;
        current.Wifi = incoming.Wifi;
        current.Browsers = incoming.Browsers;
        current.Accounts = incoming.Accounts ?? new();
        current.Cooling.Minutes = incoming.Cooling.Minutes;
        if (GuardianConfig.ActivityHourChoices.Contains(incoming.ActivityHours)) current.ActivityHours = incoming.ActivityHours;
    }

    /// <summary>Switches that "release all restrictions" turns off for the whole computer.</summary>
    private static readonly string[] ReleaseKeys =
        ["apps.enabled", "apps.blockAdminTools", "apps.blockVideo", "browser.noPrivate", "network.enabled", "boot.lockdown",
         "isp.required", "vpn.block", "wifi.knownOnly", "clock.locked", "accounts.blockNew"];

    /// <summary>
    /// "Release all restrictions": every blocking switch off, for the whole computer and every account, and no
    /// lock hours. The lists stay as they are, and the settings before the release are kept so they can come back.
    /// </summary>
    public static void ReleaseAll(GuardianConfig cfg)
    {
        cfg.ReleasedSnapshot ??= Export(cfg);
        foreach (var k in ReleaseKeys) TrySetToggle(cfg, k, false, out _);
        foreach (var u in cfg.UserPolicies)
        { u.Apps.Enabled = false; u.Apps.BlockAdminTools = false; u.Apps.BlockVideoFiles = false; }
        cfg.UserLockouts = [];
    }

    /// <summary>Brings back the settings from before "release all restrictions".</summary>
    public static bool TryUndoRelease(GuardianConfig cfg)
    {
        if (cfg.ReleasedSnapshot is not { } snap) return false;
        Import(cfg, snap);
        cfg.ReleasedSnapshot = null;
        return true;
    }

    /// <summary>Admin may change the admin password; only the recovery password may change the recovery password.</summary>
    public static bool TrySetPassword(GuardianConfig cfg, AuthResult auth, string which, string newPassword, out string error)
    {
        error = "";
        if (newPassword.Length < 8) { error = "Password must be at least 8 characters."; return false; }
        switch (which)
        {
            case "admin" when auth is AuthResult.Admin or AuthResult.Recovery:
                if (PasswordHasher.Verify(newPassword, cfg.RecoveryPasswordHash)) { error = "Must differ from the recovery password."; return false; }
                cfg.AdminPasswordHash = PasswordHasher.Hash(newPassword);
                return true;
            case "recovery" when auth is AuthResult.Recovery:
                if (PasswordHasher.Verify(newPassword, cfg.AdminPasswordHash)) { error = "Must differ from the admin password."; return false; }
                cfg.RecoveryPasswordHash = PasswordHasher.Hash(newPassword);
                return true;
            case "recovery":
                error = "Only the recovery password can change the recovery password."; return false;
            default:
                error = "Unknown password kind (admin|recovery)."; return false;
        }
    }

    public static readonly string[] Toggles =
        ["apps.enabled", "apps.defaultDeny", "apps.trustProgramFiles", "network.enabled", "network.urlFiltering", "boot.lockdown", "isp.required", "isp.netfree", "isp.etrog", "clock.locked", "vpn.block", "wifi.knownOnly", "apps.blockAdminTools", "apps.blockVideo", "browser.noPrivate", "accounts.blockNew"];

    /// <summary>Turns one feature on or off.</summary>
    public static bool TrySetToggle(GuardianConfig cfg, string key, bool value, out string error)
    {
        error = "";
        switch (key)
        {
            case "apps.enabled": cfg.Apps.Enabled = value; return true;
            case "apps.defaultDeny": cfg.Apps.DefaultDeny = value; return true;
            case "apps.trustProgramFiles": cfg.Apps.TrustProgramFiles = value; return true;
            case "apps.blockAdminTools": cfg.Apps.BlockAdminTools = value; return true;
            case "apps.blockVideo": cfg.Apps.BlockVideoFiles = value; return true;
            case "browser.noPrivate": cfg.Browsers.NoPrivate = value; return true;
            case "network.enabled":
                cfg.Network.Enabled = value;
                if (!value) cfg.Network.UrlFiltering = false;
                return true;
            case "network.urlFiltering":
                cfg.Network.UrlFiltering = value;
                if (value) cfg.Network.Enabled = true; // the proxy relies on the network layer
                return true;
            case "boot.lockdown": cfg.Boot.Lockdown = value; return true;
            case "isp.required":
                cfg.IspFilter.Required = value;
                if (value) cfg.Network.Enabled = true; // the firewall part lives in the network layer
                return true;
            case "isp.netfree":
            case "isp.etrog":
            {
                var provider = key == "isp.netfree" ? "NetFree" : "Etrog";
                cfg.IspFilter.Providers.RemoveAll(p => string.Equals(p, provider, StringComparison.OrdinalIgnoreCase));
                if (value) cfg.IspFilter.Providers.Add(provider);
                return true;
            }
            case "vpn.block":
                cfg.Vpn.Block = value;
                if (value) cfg.Network.Enabled = true; // VPN sites are refused by the DNS filter, ports by the firewall
                return true;
            case "wifi.knownOnly": cfg.Wifi.KnownOnly = value; return true;
            case "accounts.blockNew": cfg.Accounts.BlockNew = value; return true;
            case "clock.locked":
                cfg.Clock.Locked = value;
                // The time zone in effect now is the one kept (changing it is another way to move the clock).
                if (value) cfg.Clock.TimeZoneId = TimeZoneInfo.Local.Id;
                return true;
            default: error = "Unknown setting."; return false;
        }
    }

    public const string ProtectedError = "זה רכיב חיוני של ווינדוס, ואי אפשר לחסום אותו.";

    /// <summary>Allow/Block app rules by path or file name ("allowApps" / "blockApps").</summary>
    public static bool TryEditAppList(GuardianConfig cfg, string list, string action, string value, out string error)
    {
        error = "";
        value = value.Trim().Trim('"');
        var kind = list switch { "allowApps" => RuleAction.Allow, "blockApps" => RuleAction.Block, _ => (RuleAction?)null };
        if (kind is null) { error = "Unknown list."; return false; }
        if (value.Length is 0 or > 500 || value.Any(char.IsControl)) { error = "Invalid value."; return false; }
        var isPath = value.Contains('\\') || value.Contains('/');
        if (kind == RuleAction.Block && action == "add" && (Policy.ProtectedApps.IsProtectedName(value) || Policy.ProtectedApps.IsProtected(value)))
        { error = ProtectedError; return false; }
        bool Same(AppRule r) => r.Action == kind && r.When.Windows.Count == 0 &&
            string.Equals(isPath ? r.PathPrefix : r.FileName, value, StringComparison.OrdinalIgnoreCase);
        // A scanned folder is one entry in the list: removing it removes all of its programs.
        if (action == "remove" && cfg.Apps.Rules.RemoveAll(r => r.Action == kind && string.Equals(r.Source, value, StringComparison.OrdinalIgnoreCase)) > 0)
            return true;
        var existing = cfg.Apps.Rules.FindIndex(Same);
        switch (action)
        {
            case "add" when existing < 0:
                cfg.Apps.Rules.Add(isPath ? new AppRule { Action = kind.Value, PathPrefix = value } : new AppRule { Action = kind.Value, FileName = value });
                return true;
            case "add": return true;
            case "remove" when existing >= 0: cfg.Apps.Rules.RemoveAt(existing); return true;
            case "remove": error = "Not in list."; return false;
            default: error = "Unknown action."; return false;
        }
    }

    /// <summary>
    /// The folder to allow for an app, so its helpers and updated versions are allowed too.
    /// Versioned sub-folders ("app-1.0.9", "1.2.3", "current") are skipped upwards:
    /// C:\Users\me\AppData\Local\Discord\app-1.0.9\Discord.exe -> C:\Users\me\AppData\Local\Discord\
    /// </summary>
    public static string AppFolderOf(string exePath)
    {
        var dir = exePath.Replace('/', '\\');
        var cut = dir.LastIndexOf('\\');
        if (cut <= 0) return exePath;
        dir = dir[..cut];
        while (true)
        {
            var i = dir.LastIndexOf('\\');
            if (i <= 2) break;
            var name = dir[(i + 1)..];
            var versioned = System.Text.RegularExpressions.Regex.IsMatch(name, @"^(app-)?v?\d+(\.\d+)+$|^(current|bin|x64|x86)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!versioned) break;
            dir = dir[..i];
        }
        return dir + "\\";
    }

    /// <summary>Simple (unscheduled) app rules shown in the UI lists.</summary>
    public static List<string> SimpleAppList(GuardianConfig cfg, RuleAction kind) =>
        cfg.Apps.Rules.Where(r => r.Action == kind && r.When.Windows.Count == 0 && (r.Sha256 is null || r.PathPrefix != null))
            .Select(r => r.Source ?? r.PathPrefix ?? r.FileName ?? "").Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// Allows the programs found in a folder, one by one, instead of the folder itself: a program copied into
    /// it later is not allowed. <paramref name="files"/> are the programs with their contents' hash, or null
    /// in folders only administrators can change (Program Files), where the place is enough.
    /// Scanning the same folder again replaces its earlier list (new and updated programs come in).
    /// </summary>
    public static int AllowScanned(GuardianConfig cfg, string folder, IEnumerable<(string Path, string? Sha256)> files) =>
        AllowScanned(cfg, folder, files.Select(f => (f.Path, f.Sha256, (string?)null)));

    /// <summary>
    /// As above, with the publisher of each signed program: signed programs are allowed by publisher in the folder
    /// (one rule per publisher, so updates keep working), unsigned ones by their contents.
    /// </summary>
    public static int AllowScanned(GuardianConfig cfg, string folder, IEnumerable<(string Path, string? Sha256, string? Signer)> files)
    {
        folder = folder.Replace('/', '\\').TrimEnd('\\') + "\\";
        cfg.Apps.Rules.RemoveAll(r => r.Action == RuleAction.Allow && string.Equals(r.Source, folder, StringComparison.OrdinalIgnoreCase));
        cfg.Apps.Rules.RemoveAll(r => r.Action == RuleAction.Allow && r.Source is null && r.When.Windows.Count == 0 &&
                                      string.Equals(r.PathPrefix?.TrimEnd('\\') + "\\", folder, StringComparison.OrdinalIgnoreCase));
        var n = 0;
        var signers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, hash, signer) in files)
        {
            n++;
            if (signer is { Length: > 0 })
            {
                if (signers.Add(signer)) cfg.Apps.Rules.Add(new AppRule { Action = RuleAction.Allow, PathPrefix = folder, Signer = signer, Source = folder });
                continue;
            }
            cfg.Apps.Rules.Add(new AppRule { Action = RuleAction.Allow, PathPrefix = path, Sha256 = hash, Source = folder });
        }
        return n;
    }

    /// <summary>Folders only administrators can change: there the place of a program is enough to trust it.</summary>
    public static bool IsAdminOnlyFolder(string path, IEnumerable<string> adminRoots) =>
        adminRoots.Where(r => !string.IsNullOrEmpty(r)).Any(r => path.Replace('/', '\\').StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// "Block this" / "Allow this" for a site or page, whatever the list mode is.
    /// <paramref name="target"/> is a domain ("youtube.com") or a page/prefix ("youtube.com/shorts").
    /// </summary>
    public static void SetSiteAccess(NetworkPolicy p, string target, bool allow)
    {
        target = NormalizeTarget(target);
        if (target.Length == 0) return;
        var listMeansAllow = p.Mode == UrlMode.Allowlist;
        static bool Same(string a, string b) => NormalizeTarget(a) == NormalizeTarget(b);
        if (allow == listMeansAllow)
        {
            p.Exceptions.RemoveAll(e => Same(e, target));
            if (!p.Domains.Any(d => Same(d, target))) p.Domains.Add(target);
        }
        else
        {
            var removed = p.Domains.RemoveAll(d => Same(d, target));
            // Still covered by a broader entry (e.g. allowing one page of a blocked site): add an exception.
            var e = Policy.UrlPolicy.ParseEntry(target);
            var stillCovered = p.Domains.Any(d => Policy.UrlPolicy.UrlMatches(d, e.Host, e.Path.Length == 0 ? "/" : e.Path));
            if ((removed == 0 || stillCovered) && !p.Exceptions.Any(x => Same(x, target))) p.Exceptions.Add(target);
        }
    }

    /// <summary>"https://www.Site.com/a/b?q=1" -> "www.site.com/a/b"; "site.com/" -> "site.com".</summary>
    public static string NormalizeTarget(string raw)
    {
        var e = Policy.UrlPolicy.ParseEntry(raw);
        var path = e.Path;
        var q = path.IndexOfAny(['?', '#']);
        if (q >= 0) path = path[..q];
        path = path.TrimEnd('/');
        return e.Host + path;
    }

    /// <summary>Sets (or with no windows, removes) the lock-out schedule of one Windows account.</summary>
    public static bool TrySetLockout(GuardianConfig cfg, string user, IEnumerable<Scheduling.TimeWindow> windows, int warningMinutes, out string error, int breakEveryMinutes = 0,
        IEnumerable<Scheduling.TimeWindow>? breakWindows = null)
    {
        error = "";
        user = user.Trim();
        if (user.Length is 0 or > 104 || user.Any(ch => char.IsControl(ch) || "\\/\"[]:|<>+=;,?*@".Contains(ch)))
        { error = "Invalid user name."; return false; }
        var list = windows.ToList();
        if (list.Any(w => w.Days.Length == 0)) { error = "Pick at least one day for each time range."; return false; }
        if (warningMinutes is < 0 or > 60) { error = "Warning must be 0-60 minutes."; return false; }
        var breakList = (breakWindows ?? []).ToList();
        if (breakList.Any(w => w.Days.Length == 0)) { error = "Pick at least one day for each time range."; return false; }
        if (breakEveryMinutes != 0 && breakEveryMinutes is < 5 or > 600) { error = "זמן השימוש הרציף צריך להיות בין 5 ל-600 דקות."; return false; }
        cfg.UserLockouts.RemoveAll(r => Lockout.LockoutPlanner.SameUser(r.UserName, user));
        if (list.Count > 0 || breakEveryMinutes > 0)
            cfg.UserLockouts.Add(new UserLockoutRule
            {
                UserName = user, WarningMinutes = warningMinutes, BreakEveryMinutes = breakEveryMinutes,
                BreakDuring = new Scheduling.WeeklySchedule { Windows = breakEveryMinutes > 0 ? breakList : [] },
                LockedDuring = new Scheduling.WeeklySchedule { Windows = list },
            });
        return true;
    }

    /// <summary>True if, with this config, every given account has some lock-out schedule.</summary>
    public static bool EveryAccountHasLockout(GuardianConfig cfg, IEnumerable<string> accounts) =>
        accounts.Any() && accounts.All(a => cfg.UserLockouts.Any(r => Lockout.LockoutPlanner.SameUser(r.UserName, a) && r.LockedDuring.Windows.Count > 0));

    /// <summary>Adds/removes a Wi-Fi network name that may still be joined while "known networks only" is on.</summary>
    public static bool TryEditWifiList(GuardianConfig cfg, string action, string ssid, out string error)
    {
        error = "";
        ssid = ssid.Trim();
        if (!Network.WifiFilters.IsValidSsid(ssid)) { error = "שם רשת לא תקין."; return false; }
        var i = cfg.Wifi.Allowed.FindIndex(x => x == ssid);
        switch (action)
        {
            case "add": if (i < 0) cfg.Wifi.Allowed.Add(ssid); return true;
            case "remove": if (i >= 0) cfg.Wifi.Allowed.RemoveAt(i); return true;
            default: error = "Unknown action."; return false;
        }
    }

    /// <summary>Adds/removes one entry of a network list ("domains", "noDecrypt", "blockedApps", "directApps").</summary>
    /// <summary>
    /// Switches between block-list and allow-list. Each mode has its own sites and exceptions: the current ones
    /// are kept aside and the ones saved for the other mode come back, so switching back loses nothing.
    /// </summary>
    public static void SetUrlMode(NetworkPolicy n, UrlMode mode)
    {
        if (n.Mode == mode) return;
        (n.Domains, n.OtherDomains) = (n.OtherDomains, n.Domains);
        (n.Exceptions, n.OtherExceptions) = (n.OtherExceptions, n.Exceptions);
        n.Mode = mode;
    }

    public static bool TryEditNetworkList(GuardianConfig cfg, string list, string action, string value, out string error)
    {
        error = "";
        value = value.Trim();
        var target = list switch
        {
            "domains" => cfg.Network.Domains,
            "noDecrypt" => cfg.Network.NoDecryptDomains,
            "exceptions" => cfg.Network.Exceptions,
            "blockedApps" => cfg.Network.BlockedAppPaths,
            "directApps" => cfg.Network.AllowDirectApps,
            "localAddresses" => cfg.Network.LocalAddresses,
            "ispNames" => cfg.IspFilter.IssuerNames,
            "ispThumbprints" => cfg.IspFilter.Thumbprints,
            "ispApps" => cfg.IspFilter.ClientApps,
            _ => null,
        };
        if (target is null) { error = "Unknown list."; return false; }
        if (value.Length is 0 or > 500 || value.Any(char.IsControl) || (list is "domains" or "noDecrypt" or "exceptions" && value.Contains(' ')))
        { error = "Invalid value."; return false; }
        if (list == "blockedApps" && action == "add" && Policy.ProtectedApps.IsProtected(value)) { error = ProtectedError; return false; }
        if (list == "localAddresses" && action == "add")
        {
            // One address, or several separated by commas or spaces ("the whole local network" button).
            var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var norm = new List<string>();
            foreach (var part in parts)
            {
                if (!Network.LocalNetwork.TryNormalize(part, out var n, out error)) return false;
                norm.Add(n);
            }
            foreach (var n in norm)
                if (!target.Contains(n, StringComparer.OrdinalIgnoreCase)) target.Add(n);
            return true;
        }
        var existing = target.FindIndex(v => v.Equals(value, StringComparison.OrdinalIgnoreCase));
        switch (action)
        {
            case "add" when existing < 0: target.Add(value); return true;
            case "add": return true;
            case "remove" when existing >= 0: target.RemoveAt(existing); return true;
            case "remove": error = "Not in list."; return false;
            default: error = "Unknown action."; return false;
        }
    }

    /// <summary>Addresses Windows Update, Microsoft Store downloads and Office updates use.</summary>
    public static readonly string[] MicrosoftUpdateDomains =
    [
        "windowsupdate.com", "update.microsoft.com", "windowsupdate.microsoft.com", "delivery.mp.microsoft.com",
        "download.microsoft.com", "officecdn.microsoft.com", "officecdn.microsoft.com.edgesuite.net",
        "officeclient.microsoft.com", "config.office.com", "go.microsoft.com",
    ];

    /// <summary>
    /// One click so Windows and Office updates keep working: the update addresses are allowed (added to an
    /// allow-list, taken off a block-list), not decrypted by the full-URL filter, and the Office updater may
    /// connect directly. Returns what changed, for the message shown.
    /// </summary>
    public static int AllowMicrosoftUpdates(GuardianConfig cfg, string officeUpdaterPath)
    {
        var n = cfg.Network;
        var changes = 0;
        bool Has(List<string> l, string v) => l.Any(x => x.Equals(v, StringComparison.OrdinalIgnoreCase));
        foreach (var d in MicrosoftUpdateDomains)
        {
            if (n.Mode == UrlMode.Allowlist)
            {
                if (!Has(n.Domains, d)) { n.Domains.Add(d); changes++; }
                changes += n.Exceptions.RemoveAll(x => Policy.UrlPolicy.HostMatches(Policy.UrlPolicy.ParseEntry(x).Host, d));
            }
            else
            {
                changes += n.Domains.RemoveAll(x => Policy.UrlPolicy.HostMatches(Policy.UrlPolicy.ParseEntry(x).Host, d));
                // Also open them if a wider entry (say "microsoft.com") is blocked.
                if (!Has(n.Exceptions, d)) { n.Exceptions.Add(d); changes++; }
            }
            if (!Has(n.NoDecryptDomains, d)) { n.NoDecryptDomains.Add(d); changes++; }
        }
        if (!Has(n.AllowDirectApps, officeUpdaterPath)) { n.AllowDirectApps.Add(officeUpdaterPath); changes++; }
        return changes;
    }

    public static void Validate(GuardianConfig c)
    {
        foreach (var u in c.UserLockouts)
            if (string.IsNullOrWhiteSpace(u.UserName)) throw new InvalidDataException("Lockout rule without userName.");
        foreach (var r in c.Apps.Rules)
            if (string.IsNullOrEmpty(r.PathPrefix) && string.IsNullOrEmpty(r.Sha256) && string.IsNullOrEmpty(r.FileName))
                throw new InvalidDataException("App rule needs pathPrefix, sha256 or fileName.");
        foreach (var d in c.Network.Domains)
            if (string.IsNullOrWhiteSpace(d) || d.Contains(' ')) throw new InvalidDataException($"Bad domain '{d}'.");
    }

    private static GuardianConfig Clone(GuardianConfig c) =>
        JsonSerializer.Deserialize<GuardianConfig>(JsonSerializer.Serialize(c, EncryptedConfigStore.Json), EncryptedConfigStore.Json)!;
}
