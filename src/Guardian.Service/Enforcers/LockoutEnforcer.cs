using System.Collections.Concurrent;
using Guardian.Core.Config;
using Guardian.Core.Lockout;
using Guardian.Service.Native;

namespace Guardian.Service.Enforcers;

/// <summary>Temporary unlocks granted via the control pipe (admin password).</summary>
public sealed class LockoutExemptions
{
    public ConcurrentDictionary<string, DateTime> Until { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Temporary release of the admin tools (password, from the management window). Key "*" = everyone.</summary>
public sealed class AdminToolsExemptions
{
    public ConcurrentDictionary<string, DateTime> Until { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsExempt(string? user, DateTime now) =>
        Until.Any(e => e.Value > now && (e.Key == "*" || (user != null && LockoutPlanner.SameUser(e.Key, user))));
}

/// <summary>
/// Two layers: (1) Windows logon hours on the account, so the lock screen itself refuses sign-in
/// for fully locked hours; (2) a 5-second loop that warns and signs out sessions minute-accurately.
/// </summary>
public sealed class LockoutEnforcer(ILogger<LockoutEnforcer> log, LockoutExemptions exemptions, TrustedClock clock)
{
    private readonly LockoutPlanner _planner = new();
    private readonly BreakPlanner _breaks = new();
    private readonly HashSet<string> _managedUsers = new(StringComparer.OrdinalIgnoreCase);
    private string _appliedSignature = "";

    public void Tick(GuardianConfig cfg)
    {
        ApplyLogonHours(cfg);
        var now = clock.LocalNow; // not the Windows clock: changing the time must not open a locked account
        var sessions = Wts.UserSessions();
        // Addiction prevention: a sign-out after N minutes of continuous use; signing in again is allowed.
        foreach (var a in _breaks.Plan(cfg.UserLockouts, sessions, clock.UtcNow,
                     u => exemptions.Until.Any(e => LockoutPlanner.SameUser(e.Key, u) && e.Value > now), now))
        {
            if (a.Kind == LockoutKind.Warn)
                Wts.Notify(a.Session.SessionId, "Geder", $"הגיע הזמן להפסקה: בעוד {a.MinutesLeft} דקות תתבצע יציאה מהחשבון. כדאי לשמור את העבודה עכשיו. אחרי היציאה אפשר להיכנס שוב.");
            else
            {
                log.LogInformation("Break time: signing out {User} (session {Id})", a.Session.UserName, a.Session.SessionId);
                Wts.Logoff(a.Session.SessionId);
            }
        }
        foreach (var a in _planner.Plan(cfg.UserLockouts, sessions, now, exemptions.Until))
        {
            if (a.Kind == LockoutKind.Warn)
            {
                log.LogInformation("Warning {User}: locking in {M} min", a.Session.UserName, a.MinutesLeft);
                Wts.Notify(a.Session.SessionId, "Geder", $"המחשב יינעל בעוד {a.MinutesLeft} דקות לפי לוח הזמנים. התוכנות הפתוחות יישארו פתוחות.");
            }
            else
            {
                // Lock the screen (like Win+L) inside the user's session; signing back in is refused by the
                // logon hours, and an unlocked screen is locked again on the next check.
                log.LogInformation("Locking {User} (session {Id})", a.Session.UserName, a.Session.SessionId);
                if (!UserLauncher.Launch(a.Session.SessionId, Environment.ProcessPath!, "lock")) Wts.Disconnect(a.Session.SessionId);
            }
        }
    }

    /// <summary>Re-applies logon hours when the rules change, and at most once an hour (DST / week boundaries).</summary>
    private void ApplyLogonHours(GuardianConfig cfg)
    {
        var exemptActive = exemptions.Until.Where(e => e.Value > clock.LocalNow).Select(e => e.Key.ToLowerInvariant()).OrderBy(x => x);
        var sig = System.Text.Json.JsonSerializer.Serialize(cfg.UserLockouts) + clock.UtcNow.ToString("yyyyMMddHH") + string.Join(",", exemptActive);
        if (sig == _appliedSignature) return;
        _appliedSignature = sig;

        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in cfg.UserLockouts)
        {
            current.Add(rule.UserName);
            var exempt = exemptions.Until.Any(e => LockoutPlanner.SameUser(e.Key, rule.UserName) && e.Value > clock.LocalNow);
            var bits = exempt ? NetUser.AllHours : LogonHours.Compute(rule.LockedDuring, TimeZoneInfo.Local, clock.UtcNow);
            var rc = NetUser.SetLogonHours(rule.UserName, bits);
            if (rc != 0) log.LogWarning("Could not set logon hours for {User} (error {Rc}); sign-out loop still applies", rule.UserName, rc);
        }
        // Users removed from the policy get unrestricted hours back.
        foreach (var gone in _managedUsers.Except(current).ToList())
            NetUser.SetLogonHours(gone, NetUser.AllHours);
        _managedUsers.Clear();
        _managedUsers.UnionWith(current);
    }

    /// <summary>On uninstall: give every managed account its hours back.</summary>
    public static void RestoreAll(GuardianConfig cfg)
    {
        foreach (var r in cfg.UserLockouts) NetUser.SetLogonHours(r.UserName, NetUser.AllHours);
    }
}
