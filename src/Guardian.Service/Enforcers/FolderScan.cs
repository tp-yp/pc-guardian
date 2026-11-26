using System.Security.Cryptography;
using Guardian.Core.Config;
using Guardian.Core.Policy;

namespace Guardian.Service.Enforcers;

/// <summary>
/// "Allow a folder" scans the programs in it and allows each one, instead of the whole folder, so a program
/// copied into the folder later does not run. In folders anyone can write to, each program is also pinned by
/// its contents (SHA-256), so replacing an allowed file with another program does not work either.
/// </summary>
public static class FolderScan
{
    private const int MaxFiles = 3000;

    public static string[] AdminRoots =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
    ];

    public static List<(string Path, string? Sha256)> Scan(string folder)
    {
        var pinned = !ConfigEditing.IsAdminOnlyFolder(folder, AdminRoots);
        var result = new List<(string, string?)>();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var f in Directory.EnumerateFiles(folder, "*.exe", opts))
        {
            if (result.Count >= MaxFiles) break;
            if (f.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add((f, pinned ? Hash(f) : null));
        }
        return result.Where(r => !pinned || r.Item2 != null).ToList();
    }

    public static string? Hash(string path)
    {
        try { using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return Convert.ToHexString(SHA256.HashData(fs)); }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Once, after updating from 0.9.6 or older: allowed folders become scanned lists, and "Program Files counts as
    /// allowed" (in allow-list mode) becomes a scanned list of Program Files, so nothing that ran before is closed.
    /// </summary>
    public static bool MigrateOldRules(GuardianConfig cfg)
    {
        var changed = false;
        foreach (var apps in new[] { cfg.Apps }.Concat(cfg.UserPolicies.Select(u => u.Apps)))
        {
            var folders = apps.Rules.Where(r => r.Action == RuleAction.Allow && r.Source is null && r.Sha256 is null && r.When.Windows.Count == 0 &&
                                                r.PathPrefix is { Length: > 0 } p && !p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                    .Select(r => r.PathPrefix!).ToList();
            if (apps.TrustProgramFiles && apps.DefaultDeny)
                folders.AddRange(AdminRoots.Take(2).Where(d => !string.IsNullOrEmpty(d)));
            if (apps.TrustProgramFiles) { apps.TrustProgramFiles = false; changed = true; }
            foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(folder)) continue;
                var tmp = new GuardianConfig { Apps = apps };
                ConfigEditing.AllowScanned(tmp, folder, Scan(folder));
                changed = true;
            }
        }
        return changed;
    }
}
