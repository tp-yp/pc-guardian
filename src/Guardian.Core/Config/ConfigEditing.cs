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

    /// <summary>Simple (unscheduled) app rules shown in the UI lists.</summary>
    public static List<string> SimpleAppList(GuardianConfig cfg, RuleAction kind) =>
        cfg.Apps.Rules.Where(r => r.Action == kind && r.When.Windows.Count == 0 && r.Sha256 is null)
            .Select(r => r.PathPrefix ?? r.FileName ?? "").Where(v => v.Length > 0).ToList();

    /// <summary>Adds/removes one entry of a network list ("domains", "noDecrypt", "blockedApps", "directApps").</summary>
    public static bool TryEditNetworkList(GuardianConfig cfg, string list, string action, string value, out string error)
    {
        error = "";
        value = value.Trim();
        var target = list switch
        {
            "domains" => cfg.Network.Domains,
            "noDecrypt" => cfg.Network.NoDecryptDomains,
            "blockedApps" => cfg.Network.BlockedAppPaths,
            "directApps" => cfg.Network.AllowDirectApps,
            _ => null,
        };
        if (target is null) { error = "Unknown list."; return false; }
        if (value.Length is 0 or > 500 || value.Any(char.IsControl) || (list is "domains" or "noDecrypt" && value.Contains(' ')))
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
