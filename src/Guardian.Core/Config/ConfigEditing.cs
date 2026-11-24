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
        return JsonSerializer.Serialize(copy, EncryptedConfigStore.Json);
    }

    /// <summary>Replaces the policy parts of <paramref name="current"/> with those from <paramref name="json"/>.
    /// Password hashes and the service name are kept from the current config.</summary>
    public static void Import(GuardianConfig current, string json)
    {
        var incoming = JsonSerializer.Deserialize<GuardianConfig>(json, EncryptedConfigStore.Json)
                       ?? throw new InvalidDataException("Empty config.");
        Validate(incoming);
        current.UserLockouts = incoming.UserLockouts;
        current.Apps = incoming.Apps;
        current.Network = incoming.Network;
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
        ["apps.enabled", "apps.defaultDeny", "apps.trustProgramFiles", "network.enabled", "network.urlFiltering"];

    /// <summary>Turns one feature on or off.</summary>
    public static bool TrySetToggle(GuardianConfig cfg, string key, bool value, out string error)
    {
        error = "";
        switch (key)
        {
            case "apps.enabled": cfg.Apps.Enabled = value; return true;
            case "apps.defaultDeny": cfg.Apps.DefaultDeny = value; return true;
            case "apps.trustProgramFiles": cfg.Apps.TrustProgramFiles = value; return true;
            case "network.enabled":
                cfg.Network.Enabled = value;
                if (!value) cfg.Network.UrlFiltering = false;
                return true;
            case "network.urlFiltering":
                cfg.Network.UrlFiltering = value;
                if (value) cfg.Network.Enabled = true; // the proxy relies on the network layer
                return true;
            default: error = "Unknown setting."; return false;
        }
    }

    /// <summary>Allow/Block app rules by path or file name ("allowApps" / "blockApps").</summary>
    public static bool TryEditAppList(GuardianConfig cfg, string list, string action, string value, out string error)
    {
        error = "";
        value = value.Trim().Trim('"');
        var kind = list switch { "allowApps" => RuleAction.Allow, "blockApps" => RuleAction.Block, _ => (RuleAction?)null };
        if (kind is null) { error = "Unknown list."; return false; }
        if (value.Length is 0 or > 500 || value.Any(char.IsControl)) { error = "Invalid value."; return false; }
        var isPath = value.Contains('\\') || value.Contains('/');
        bool Same(AppRule r) => r.Action == kind && r.When.Windows.Count == 0 &&
            string.Equals(isPath ? r.PathPrefix : r.FileName, value, StringComparison.OrdinalIgnoreCase);
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
        cfg.Apps.Rules.Where(r => r.Action == kind && r.When.Windows.Count == 0 && r.Sha256 is null)
            .Select(r => r.PathPrefix ?? r.FileName ?? "").Where(v => v.Length > 0).ToList();

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
    public static bool TrySetLockout(GuardianConfig cfg, string user, IEnumerable<Scheduling.TimeWindow> windows, int warningMinutes, out string error)
    {
        error = "";
        user = user.Trim();
        if (user.Length is 0 or > 104 || user.Any(ch => char.IsControl(ch) || "\\/\"[]:|<>+=;,?*@".Contains(ch)))
        { error = "Invalid user name."; return false; }
        var list = windows.ToList();
        if (list.Any(w => w.Days.Length == 0)) { error = "Pick at least one day for each time range."; return false; }
        if (warningMinutes is < 0 or > 60) { error = "Warning must be 0-60 minutes."; return false; }
        cfg.UserLockouts.RemoveAll(r => Lockout.LockoutPlanner.SameUser(r.UserName, user));
        if (list.Count > 0)
            cfg.UserLockouts.Add(new UserLockoutRule
            {
                UserName = user, WarningMinutes = warningMinutes,
                LockedDuring = new Scheduling.WeeklySchedule { Windows = list },
            });
        return true;
    }

    /// <summary>True if, with this config, every given account has some lock-out schedule.</summary>
    public static bool EveryAccountHasLockout(GuardianConfig cfg, IEnumerable<string> accounts) =>
        accounts.Any() && accounts.All(a => cfg.UserLockouts.Any(r => Lockout.LockoutPlanner.SameUser(r.UserName, a) && r.LockedDuring.Windows.Count > 0));

    /// <summary>Adds/removes one entry of a network list ("domains", "noDecrypt", "blockedApps", "directApps").</summary>
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
            _ => null,
        };
        if (target is null) { error = "Unknown list."; return false; }
        if (value.Length is 0 or > 500 || value.Any(char.IsControl) || (list is "domains" or "noDecrypt" or "exceptions" && value.Contains(' ')))
        { error = "Invalid value."; return false; }
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
