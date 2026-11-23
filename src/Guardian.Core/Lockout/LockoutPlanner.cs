using Guardian.Core.Config;

namespace Guardian.Core.Lockout;

public sealed record UserSession(int SessionId, string UserName);

public enum LockoutKind { Warn, Logoff }

public sealed record LockoutAction(LockoutKind Kind, UserSession Session, int MinutesLeft = 0);

/// <summary>Decides which sessions to warn or sign out right now.</summary>
public sealed class LockoutPlanner
{
    private readonly Dictionary<(int, string), DateTime> _warned = new();

    /// <param name="exemptUntil">Temporary unlocks granted with the admin password (user -> local time).</param>
    public IReadOnlyList<LockoutAction> Plan(IEnumerable<UserLockoutRule> rules, IEnumerable<UserSession> sessions,
        DateTime local, IReadOnlyDictionary<string, DateTime>? exemptUntil = null)
    {
        var actions = new List<LockoutAction>();
        foreach (var s in sessions)
        {
            if (exemptUntil != null && exemptUntil.Any(e => SameUser(e.Key, s.UserName) && local < e.Value))
                continue;
            var rule = rules.FirstOrDefault(r => SameUser(r.UserName, s.UserName));
            if (rule == null) continue;

            if (rule.LockedDuring.Windows.Count > 0 && rule.LockedDuring.IsActive(local))
            {
                actions.Add(new(LockoutKind.Logoff, s));
                _warned.Remove((s.SessionId, s.UserName));
                continue;
            }

            if (rule.WarningMinutes <= 0) continue;
            var minutes = MinutesUntilLock(rule, local, rule.WarningMinutes);
            if (minutes is { } m && !_warned.ContainsKey((s.SessionId, s.UserName)))
            {
                _warned[(s.SessionId, s.UserName)] = local;
                actions.Add(new(LockoutKind.Warn, s, m));
            }
        }
        // Forget old warnings so the next lock period warns again.
        foreach (var k in _warned.Where(kv => local - kv.Value > TimeSpan.FromHours(1)).Select(kv => kv.Key).ToList())
            _warned.Remove(k);
        return actions;
    }

    /// <summary>Minutes until the lock starts, if it starts within <paramref name="horizon"/> minutes.</summary>
    public static int? MinutesUntilLock(UserLockoutRule rule, DateTime local, int horizon)
    {
        if (rule.LockedDuring.Windows.Count == 0) return null;
        for (var m = 1; m <= horizon; m++)
            if (rule.LockedDuring.IsActive(local.AddMinutes(m))) return m;
        return null;
    }

    /// <summary>Matches "name", "MACHINE\name" or "name@domain" case-insensitively.</summary>
    public static bool SameUser(string configured, string actual) =>
        string.Equals(Strip(configured), Strip(actual), StringComparison.OrdinalIgnoreCase);

    private static string Strip(string u)
    {
        var i = u.LastIndexOf('\\');
        if (i >= 0) u = u[(i + 1)..];
        var at = u.IndexOf('@');
        return at >= 0 ? u[..at] : u;
    }
}
