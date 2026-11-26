using Guardian.Core.Config;
using Guardian.Core.Lockout;
using Guardian.Service.Native;

namespace Guardian.Service.Enforcers;

/// <summary>
/// "New accounts stay off until approved" (see <see cref="NewAccounts"/>). Checked every two seconds.
/// The known accounts and the accounts Geder turned off are kept in GuardianData, on this computer only.
/// Turning the switch off, and removing Geder, turns those accounts on again.
/// </summary>
public sealed class AccountGuard(ILogger<AccountGuard> log, AccessLog access)
{
    private static string KnownPath => Path.Combine(Paths.DataDir, "known-accounts.txt");
    private static string OursPath => Path.Combine(Paths.DataDir, "new-accounts.txt");
    private static readonly object Gate = new();
    private DateTime _next = DateTime.MinValue;

    public void Tick(GuardianConfig cfg)
    {
        if (DateTime.UtcNow < _next) return;
        _next = DateTime.UtcNow.AddSeconds(2);
        lock (Gate)
        {
            var on = cfg.Accounts.BlockNew;
            var ours = Read(OursPath);
            if (!on && ours.Count == 0)
            {
                if (File.Exists(KnownPath)) Try(() => File.Delete(KnownPath)); // the next "on" takes a fresh list
                return;
            }
            var accounts = NetUser.List();
            if (accounts.Count == 0) return; // listing failed: change nothing
            var known = new HashSet<int>();
            if (on)
            {
                if (File.Exists(KnownPath)) known = Read(KnownPath);
                else
                {
                    known = NewAccounts.Snapshot(accounts, LockoutEnforcer.LockedByGeder());
                    Write(KnownPath, known);
                    log.LogInformation("New accounts guard: {N} accounts known", known.Count);
                }
            }
            var (disable, enable) = NewAccounts.Plan(on, accounts, known, ours);
            var changed = false;
            foreach (var a in enable)
            {
                if (NetUser.SetDisabled(a.Name, false) != 0) continue;
                ours.Remove(a.Rid); changed = true;
                log.LogInformation("Account {User} turned on again", a.Name);
            }
            foreach (var a in disable)
            {
                if (NetUser.SetDisabled(a.Name, true) != 0) { log.LogWarning("Could not turn off the new account {User}", a.Name); continue; }
                changed |= ours.Add(a.Rid);
                log.LogInformation("New account {User} turned off until it is approved", a.Name);
                access.Add("account", a.Name, allowed: false);
            }
            // Accounts that were deleted, and (when off) accounts that are already on again, are forgotten.
            changed |= ours.RemoveWhere(r => !accounts.Any(a => a.Rid == r) || (!on && accounts.Any(a => a.Rid == r && a.Enabled))) > 0;
            if (changed) Write(OursPath, ours);
        }
    }

    /// <summary>Accounts waiting for approval (turned off by Geder).</summary>
    public static List<LocalAccount> Pending()
    {
        var ours = Read(OursPath);
        return ours.Count == 0 ? [] : NetUser.List().Where(a => ours.Contains(a.Rid)).ToList();
    }

    /// <summary>Approves an account: it becomes known, and is turned on at the next check.</summary>
    public static bool Approve(int rid)
    {
        lock (Gate)
        {
            if (!Read(OursPath).Contains(rid)) return false;
            var known = Read(KnownPath);
            known.Add(rid);
            Write(KnownPath, known);
            return true;
        }
    }

    /// <summary>On uninstall: turn on every account Geder turned off as new.</summary>
    public static void RestoreAll()
    {
        var ours = Read(OursPath);
        foreach (var a in NetUser.List().Where(a => ours.Contains(a.Rid))) NetUser.SetDisabled(a.Name, false);
        Try(() => File.Delete(OursPath));
        Try(() => File.Delete(KnownPath));
    }

    private static HashSet<int> Read(string path)
    {
        try { return File.Exists(path) ? File.ReadAllLines(path).Select(l => int.TryParse(l.Split('|')[0], out var r) ? r : -1).Where(r => r >= 0).ToHashSet() : []; }
        catch (Exception) { return []; }
    }

    private static void Write(string path, HashSet<int> rids)
    {
        try { File.WriteAllLines(path, rids.Order().Select(r => r.ToString())); } catch (Exception) { /* best effort */ }
    }

    private static void Try(Action a) { try { a(); } catch (Exception) { } }
}
