using Guardian.Core.Config;
using Guardian.Core.Lockout;
using Guardian.Core.Scheduling;

namespace Guardian.Core.Tests;

public class LockoutTests
{
    // Locked nightly 22:00-07:00, every day.
    private static UserLockoutRule Rule() => new()
    {
        UserName = "kid",
        WarningMinutes = 5,
        LockedDuring = new WeeklySchedule { Windows = { new(Enum.GetValues<DayOfWeek>(), new(22, 0), new(7, 0)) } },
    };

    private static readonly UserSession Kid = new(2, @"PC\Kid");
    private static readonly UserSession Parent = new(3, "parent");
    private static readonly DateTime Mon = new(2026, 11, 23);

    [Fact]
    public void Logs_off_only_matching_user_during_lock()
    {
        var a = new LockoutPlanner().Plan([Rule()], [Kid, Parent], Mon.AddHours(23));
        Assert.Single(a);
        Assert.Equal(LockoutKind.Logoff, a[0].Kind);
        Assert.Equal(2, a[0].Session.SessionId);
    }

    [Fact]
    public void Warns_once_before_lock()
    {
        var p = new LockoutPlanner();
        var t = Mon.AddHours(21).AddMinutes(57);
        var a = p.Plan([Rule()], [Kid], t);
        Assert.Equal(LockoutKind.Warn, Assert.Single(a).Kind);
        Assert.Equal(3, a[0].MinutesLeft);
        Assert.Empty(p.Plan([Rule()], [Kid], t.AddMinutes(1)));
        Assert.Equal(LockoutKind.Logoff, Assert.Single(p.Plan([Rule()], [Kid], t.AddMinutes(3))).Kind);
    }

    [Fact]
    public void Nothing_outside_window()
        => Assert.Empty(new LockoutPlanner().Plan([Rule()], [Kid], Mon.AddHours(12)));

    [Fact]
    public void Temporary_unlock_is_respected()
    {
        var exempt = new Dictionary<string, DateTime> { ["pc\\kid".Split('\\')[1]] = Mon.AddHours(23).AddMinutes(30) };
        Assert.Empty(new LockoutPlanner().Plan([Rule()], [Kid], Mon.AddHours(23), exempt));
        Assert.Single(new LockoutPlanner().Plan([Rule()], [Kid], Mon.AddHours(23).AddMinutes(31), exempt));
    }

    [Theory]
    [InlineData("kid", @"DESKTOP-1\KID", true)]
    [InlineData("kid@outlook.com", "kid", true)]
    [InlineData("kid", "kiddo", false)]
    public void User_matching(string a, string b, bool expected) => Assert.Equal(expected, LockoutPlanner.SameUser(a, b));

    [Fact]
    public void Logon_hours_deny_full_locked_hours_only()
    {
        var bits = LogonHours.Compute(Rule().LockedDuring, TimeZoneInfo.Utc, Mon);
        Assert.Equal(21, bits.Length);
        var mondayBase = 24; // Monday 00:00 UTC
        Assert.False(LogonHours.IsAllowed(bits, mondayBase + 3));   // 03:00 locked
        Assert.False(LogonHours.IsAllowed(bits, mondayBase + 22));  // 22:00 locked
        Assert.True(LogonHours.IsAllowed(bits, mondayBase + 7));    // 07:00 free
        Assert.True(LogonHours.IsAllowed(bits, mondayBase + 21));   // 21:00 free
    }

    [Fact]
    public void Logon_hours_partial_hour_stays_allowed()
    {
        var s = new WeeklySchedule { Windows = { new([DayOfWeek.Monday], new(22, 30), new(23, 0)) } };
        var bits = LogonHours.Compute(s, TimeZoneInfo.Utc, Mon);
        Assert.True(LogonHours.IsAllowed(bits, 24 + 22));
    }
}
