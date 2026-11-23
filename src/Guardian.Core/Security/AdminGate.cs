using Guardian.Core.Config;

namespace Guardian.Core.Security;

public enum AuthResult { Denied, Admin, Recovery, LockedOut }

/// <summary>Checks the admin / recovery password with brute-force throttling.</summary>
public sealed class AdminGate
{
    private readonly Func<DateTime> _now;
    private int _failures;
    private DateTime _lockedUntil = DateTime.MinValue;

    public int MaxFailures { get; init; } = 5;
    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(15);

    public AdminGate(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    public AuthResult Authenticate(GuardianConfig config, string password)
    {
        if (_now() < _lockedUntil) return AuthResult.LockedOut;
        // Recovery password is checked first so it always works as the master key.
        if (PasswordHasher.Verify(password, config.RecoveryPasswordHash)) { _failures = 0; return AuthResult.Recovery; }
        if (PasswordHasher.Verify(password, config.AdminPasswordHash)) { _failures = 0; return AuthResult.Admin; }
        if (++_failures >= MaxFailures) { _failures = 0; _lockedUntil = _now() + LockoutDuration; return AuthResult.LockedOut; }
        return AuthResult.Denied;
    }
}
