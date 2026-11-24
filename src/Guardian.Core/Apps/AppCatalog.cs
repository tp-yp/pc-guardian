using Guardian.Core.Config;

namespace Guardian.Core.Apps;

public enum AppSource { StartMenu, InstalledApps, Store }

public sealed record CatalogApp(string Name, string ExePath, AppSource Source);

/// <summary>An app with its current Guardian state, as shown in the "Programs" screen.</summary>
public sealed record CatalogEntry(string Name, string ExePath, AppSource Source, bool Locked, bool InternetBlocked, bool DirectInternet);

/// <summary>Merges apps found in the Start menu, "Installed apps" and the Store into one list.</summary>
public static class AppCatalog
{
    private static readonly string[] Noise = ["uninstall", "uninst", "הסר", "readme", "help", "setup", "update", "repair", "crash", "report"];

    public static List<CatalogApp> Merge(IEnumerable<CatalogApp> found)
    {
        var byPath = new Dictionary<string, CatalogApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in found)
        {
            if (string.IsNullOrWhiteSpace(a.Name) || !a.ExePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            var file = Path.GetFileNameWithoutExtension(a.ExePath.Replace('\\', '/').Split('/')[^1]);
            if (Noise.Any(n => a.Name.Contains(n, StringComparison.OrdinalIgnoreCase) || file.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
            var key = a.ExePath.Replace('/', '\\');
            // Start-menu names are the ones people recognise, so they win over registry names.
            if (!byPath.TryGetValue(key, out var existing) || a.Source < existing.Source)
                byPath[key] = a with { ExePath = key, Name = a.Name.Trim() };
        }
        return byPath.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Protected parts of Windows (File Explorer and the like) are not listed: they cannot be restricted.</summary>
    public static List<CatalogEntry> WithState(IEnumerable<CatalogApp> apps, GuardianConfig cfg) =>
        apps.Where(a => !Policy.ProtectedApps.IsProtected(a.ExePath)).Select(a => new CatalogEntry(a.Name, a.ExePath, a.Source,
            Locked: IsLocked(cfg, a.ExePath),
            InternetBlocked: cfg.Network.BlockedAppPaths.Contains(a.ExePath, StringComparer.OrdinalIgnoreCase),
            DirectInternet: cfg.Network.AllowDirectApps.Contains(a.ExePath, StringComparer.OrdinalIgnoreCase))).ToList();

    public static bool IsLocked(GuardianConfig cfg, string exe) =>
        cfg.Apps.Rules.Any(r => r.Action == RuleAction.Block && r.When.Windows.Count == 0 &&
                                string.Equals(r.PathPrefix, exe, StringComparison.OrdinalIgnoreCase));

    /// <summary>Applies one switch of the Programs screen. Locking also turns app blocking on.</summary>
    /// <param name="originalName">The name written inside the program, stored with a lock so renaming does not undo it.</param>
    public static bool TrySet(GuardianConfig cfg, string exe, string kind, bool on, out string error, string? originalName = null)
    {
        error = "";
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || exe.Any(char.IsControl) || exe.Length > 500)
        { error = "Invalid program."; return false; }
        if (on && kind is "lock" or "internet" && Policy.ProtectedApps.IsProtected(exe)) { error = Config.ConfigEditing.ProtectedError; return false; }
        switch (kind)
        {
            case "lock":
                cfg.Apps.Rules.RemoveAll(r => r.Action == RuleAction.Block && r.When.Windows.Count == 0 &&
                                              string.Equals(r.PathPrefix, exe, StringComparison.OrdinalIgnoreCase));
                if (on)
                {
                    cfg.Apps.Rules.Add(new AppRule { Action = RuleAction.Block, PathPrefix = exe, OriginalName = string.IsNullOrWhiteSpace(originalName) ? null : originalName.Trim() });
                    cfg.Apps.Enabled = true;
                }
                return true;
            case "internet":
                SetIn(cfg.Network.BlockedAppPaths, exe, on);
                if (on) cfg.Network.Enabled = true;
                return true;
            case "direct":
                SetIn(cfg.Network.AllowDirectApps, exe, on);
                return true;
            default:
                error = "Unknown switch."; return false;
        }
    }

    private static void SetIn(List<string> list, string value, bool on)
    {
        list.RemoveAll(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
        if (on) list.Add(value);
    }
}
