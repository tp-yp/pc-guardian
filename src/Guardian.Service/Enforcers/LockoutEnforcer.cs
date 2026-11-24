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

/// <summary>
/// Two layers: (1) Windows logon hours on the account, so the lock screen itself refuses sign-in
/// for fully locked hours; (2) a 5-second loop that warns and signs out sessions minute-accurately.
/// </summary>
public sealed class LockoutEnforcer(ILogger<LockoutEnforcer> log, LockoutExemptions exemptions)
{
    private readonly LockoutPlanner _planner = new();
    private readonly HashSet<string> _managedUsers = new(StringComparer.OrdinalIgnoreCase);
    private string _appliedSignature = "";

    public void Tick(GuardianConfig cfg)
    {
        ApplyLogonHours(cfg);
        var now = DateTime.Now;
        foreach (var a in _planner.Plan(cfg.UserLockouts, Wts.UserSessions(), now, exemptions.Until))
        {
            if (a.Kind == LockoutKind.Warn)
            {
                log.LogInformation("Warning {User}: locking in {M} min", a.Session.UserName, a.MinutesLeft);
                Wts.Notify(a.Session.SessionId, "Guardian", $"החשבון ייסגר בעוד {a.MinutesLeft} דקות לפי לוח הזמנים. כדאי לשמור את העבודה עכשיו.");
            }
            else
            {
                log.LogInformation("Signing out {User} (session {Id})", a.Session.UserName, a.Session.SessionId);
                Wts.Logoff(a.Session.SessionId);
            }
        }
    }

    /// <summary>Re-applies logon hours when the rules change, and at most once an hour (DST / week boundaries).</summary>
    private void ApplyLogonHours(GuardianConfig cfg)
    {
        var exemptActive = exemptions.Until.Where(e => e.Value > DateTime.Now).Select(e => e.Key.ToLowerInvariant()).OrderBy(x => x);
        var sig = System.Text.Json.JsonSerializer.Serialize(cfg.UserLockouts) + DateTime.UtcNow.ToString("yyyyMMddHH") + string.Join(",", exemptActive);
        if (sig == _appliedSignature) return;
        _appliedSignature = sig;

        var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in cfg.UserLockouts)
        {
            current.Add(rule.UserName);
            var exempt = exemptions.Until.Any(e => LockoutPlanner.SameUser(e.Key, rule.UserName) && e.Value > DateTime.Now);
            var bits = exempt ? NetUser.AllHours : LogonHours.Compute(rule.LockedDuring, TimeZoneInfo.Local, DateTime.UtcNow);
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
