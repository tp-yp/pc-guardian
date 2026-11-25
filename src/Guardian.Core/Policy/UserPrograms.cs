using Guardian.Core.Apps;
using Guardian.Core.Lockout;

namespace Guardian.Core.Policy;

/// <summary>
/// Programs installed for one account only live inside that account's profile folder
/// (C:\Users\name\AppData\..., Downloads, Desktop). Programs installed for everyone live elsewhere
/// (Program Files and the like). Each account sees its own programs and the shared ones; programs
/// inside another account's folder are blocked for it whenever program blocking is on.
/// </summary>
public static class UserPrograms
{
    /// <summary>The account whose profile folder holds <paramref name="exePath"/>, or null for a shared program.</summary>
    /// <param name="profiles">Profile folder (C:\Users\kid) → account name ("PC\kid").</param>
    public static string? OwnerOf(string exePath, IReadOnlyDictionary<string, string> profiles)
    {
        var p = exePath.Replace('/', '\\');
        string? best = null;
        var bestLen = 0;
        foreach (var (dir, account) in profiles)
        {
            var d = dir.Replace('/', '\\').TrimEnd('\\') + "\\";
            if (d.Length > bestLen && p.StartsWith(d, StringComparison.OrdinalIgnoreCase)) { best = account; bestLen = d.Length; }
        }
        return best;
    }

    /// <summary>True when the program is inside the profile folder of an account other than <paramref name="user"/>.</summary>
    public static bool IsOtherUsers(string exePath, string? user, IReadOnlyDictionary<string, string> profiles)
    {
        if (string.IsNullOrWhiteSpace(user)) return false;
        var owner = OwnerOf(exePath, profiles);
        return owner != null && !LockoutPlanner.SameUser(owner, user);
    }

    /// <summary>
    /// The programs screen for one account: shared programs and the account's own. With no account
    /// (the general rules) everything is listed, each with the account it belongs to.
    /// </summary>
    public static List<CatalogEntry> ForUser(IEnumerable<CatalogEntry> entries, string? user, IReadOnlyDictionary<string, string> profiles)
    {
        var list = new List<CatalogEntry>();
        foreach (var e in entries)
        {
            var owner = OwnerOf(e.ExePath, profiles);
            if (!string.IsNullOrWhiteSpace(user) && owner != null && !LockoutPlanner.SameUser(owner, user)) continue;
            list.Add(e with { Owner = owner is null ? null : ShortName(owner) });
        }
        return list;
    }

    private static string ShortName(string account)
    {
        var i = account.LastIndexOf('\\');
        return i < 0 ? account : account[(i + 1)..];
    }
}
