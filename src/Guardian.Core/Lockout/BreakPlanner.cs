using Guardian.Core.Config;

namespace Guardian.Core.Lockout;

/// <summary>
/// "Addiction prevention": after N minutes of continuous use a session is signed out. The user may
/// sign in again at once, and the count starts over. Time comes from the trusted clock (UTC).
/// </summary>
public sealed class BreakPlanner
{
    private readonly Dictionary<(int, string), (DateTime Start, bool Warned)> _sessions = new();

    /// <param name="local">Local (trusted) time, for the hours in which the rule applies.</param>
    public IReadOnlyList<LockoutAction> Plan(IEnumerable<UserLockoutRule> rules, IEnumerable<UserSession> sessions,
        DateTime utcNow, Func<string, bool>? exempt = null, DateTime? local = null)
    {
        var actions = new List<LockoutAction>();
        var seen = new HashSet<(int, string)>();
        foreach (var s in sessions)
        {
            var key = (s.SessionId, s.UserName.ToLowerInvariant());
            seen.Add(key);
            if (!_sessions.TryGetValue(key, out var st)) _sessions[key] = st = (utcNow, false);
            var rule = rules.FirstOrDefault(r => LockoutPlanner.SameUser(r.UserName, s.UserName));
            if (rule is null || rule.BreakEveryMinutes <= 0 || exempt?.Invoke(s.UserName) == true) continue;
            var when = local ?? utcNow.ToLocalTime();
            if ((rule.BreakDuring.Windows.Count > 0 && !rule.BreakDuring.IsActive(when)) ||
                (rule.LockedDuring.Windows.Count > 0 && rule.LockedDuring.IsActive(when))) // locked hours win
            {
                // Outside its hours: normal use. The count starts when the hours begin.
                _sessions[key] = (utcNow, false);
                continue;
            }

            var used = utcNow - st.Start;
            var limit = TimeSpan.FromMinutes(rule.BreakEveryMinutes);
            if (used >= limit) { actions.Add(new(LockoutKind.Logoff, s)); continue; }
            var warn = Math.Min(rule.WarningMinutes, rule.BreakEveryMinutes / 2);
            if (warn > 0 && !st.Warned && used >= limit - TimeSpan.FromMinutes(warn))
            {
                _sessions[key] = st with { Warned = true };
                actions.Add(new(LockoutKind.Warn, s, Math.Max(1, (int)Math.Ceiling((limit - used).TotalMinutes))));
            }
        }
        // A signed-out session is forgotten, so the next sign-in starts a fresh count.
        foreach (var k in _sessions.Keys.Where(k => !seen.Contains(k)).ToList()) _sessions.Remove(k);
        return actions;
    }
}
