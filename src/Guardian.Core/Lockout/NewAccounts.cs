namespace Guardian.Core.Lockout;

/// <summary>A local Windows account. <see cref="Rid"/> is its fixed number on this computer, which a rename does not change.</summary>
public sealed record LocalAccount(int Rid, string Name, bool Enabled, bool Admin);

/// <summary>
/// "New accounts stay off until approved": when the switch is turned on, every account that is on at that moment
/// (and every account Geder turned off for its locked hours) becomes known. An account that is not known and is on,
/// such as a new administrator account, is turned off until it is approved in Geder. Turning an account off never
/// leaves the computer without an administrator that can sign in. Accounts are known by number, so renaming a known
/// account and giving a new one its old name does not help.
/// </summary>
public static class NewAccounts
{
    public static HashSet<int> Snapshot(IEnumerable<LocalAccount> accounts, IEnumerable<string> lockedByGeder)
    {
        var locked = new HashSet<string>(lockedByGeder.Select(Strip), StringComparer.OrdinalIgnoreCase);
        return accounts.Where(a => a.Enabled || locked.Contains(a.Name)).Select(a => a.Rid).ToHashSet();
    }

    /// <param name="ours">Accounts Geder turned off because they were not known.</param>
    public static (List<LocalAccount> Disable, List<LocalAccount> Enable) Plan(bool on, IReadOnlyList<LocalAccount> accounts,
        IReadOnlySet<int> known, IReadOnlySet<int> ours)
    {
        var enable = accounts.Where(a => ours.Contains(a.Rid) && !a.Enabled && (!on || known.Contains(a.Rid))).ToList();
        var disable = new List<LocalAccount>();
        if (!on) return (disable, enable);
        var admins = accounts.Count(a => a.Enabled && a.Admin);
        foreach (var a in accounts.Where(a => a.Enabled && !known.Contains(a.Rid)))
        {
            if (a.Admin)
            {
                if (admins <= 1) continue; // the last administrator that can sign in stays on
                admins--;
            }
            disable.Add(a);
        }
        return (disable, enable);
    }

    private static string Strip(string u) => u.Contains('\\') ? u[(u.LastIndexOf('\\') + 1)..] : u;
}
