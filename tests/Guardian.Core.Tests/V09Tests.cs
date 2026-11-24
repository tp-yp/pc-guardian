using Guardian.Core.Apps;
using Guardian.Core.Config;
using Guardian.Core.Lockout;
using Guardian.Core.Network;
using Guardian.Core.Policy;
using Guardian.Core.Scheduling;
using Xunit;

namespace Guardian.Core.Tests;

public class BreakPlannerTests
{
    private static readonly UserSession Kid = new(2, @"PC\kid");
    private static List<UserLockoutRule> Rules(int every, int warn = 5) => [new() { UserName = "kid", BreakEveryMinutes = every, WarningMinutes = warn }];

    [Fact]
    public void Warns_then_signs_out_after_continuous_use_and_restarts_count_after_sign_in()
    {
        var p = new BreakPlanner();
        var t0 = new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);
        Assert.Empty(p.Plan(Rules(45), [Kid], t0));
        Assert.Empty(p.Plan(Rules(45), [Kid], t0.AddMinutes(39)));
        var warn = Assert.Single(p.Plan(Rules(45), [Kid], t0.AddMinutes(40)));
        Assert.Equal(LockoutKind.Warn, warn.Kind);
        Assert.Equal(5, warn.MinutesLeft);
        Assert.Empty(p.Plan(Rules(45), [Kid], t0.AddMinutes(41))); // warned once
        Assert.Equal(LockoutKind.Logoff, Assert.Single(p.Plan(Rules(45), [Kid], t0.AddMinutes(45))).Kind);
        // Signed out (session gone), then signs in again: a fresh 45 minutes.
        Assert.Empty(p.Plan(Rules(45), [], t0.AddMinutes(46)));
        Assert.Empty(p.Plan(Rules(45), [Kid], t0.AddMinutes(47)));
        Assert.Empty(p.Plan(Rules(45), [Kid], t0.AddMinutes(80)));
        Assert.Equal(LockoutKind.Logoff, Assert.Single(p.Plan(Rules(45), [Kid], t0.AddMinutes(92))).Kind);
    }

    [Fact]
    public void Off_or_exempt_users_are_left_alone()
    {
        var p = new BreakPlanner();
        var t0 = DateTime.UtcNow;
        p.Plan(Rules(0), [Kid], t0);
        Assert.Empty(p.Plan(Rules(0), [Kid], t0.AddHours(5)));
        var q = new BreakPlanner();
        q.Plan(Rules(30), [Kid], t0);
        Assert.Empty(q.Plan(Rules(30), [Kid], t0.AddHours(1), _ => true));
    }

    [Fact]
    public void Break_only_rule_is_kept_and_does_not_lock_logon_hours()
    {
        var cfg = new GuardianConfig();
        Assert.True(ConfigEditing.TrySetLockout(cfg, "kid", [], 5, out _, breakEveryMinutes: 60));
        var r = Assert.Single(cfg.UserLockouts);
        Assert.Equal(60, r.BreakEveryMinutes);
        Assert.Empty(r.LockedDuring.Windows);
        // No windows: logon hours stay fully open, and the lockout planner never signs out.
        var bits = LogonHours.Compute(r.LockedDuring, TimeZoneInfo.Utc, DateTime.UtcNow);
        Assert.All(Enumerable.Range(0, 168), h => Assert.True(LogonHours.IsAllowed(bits, h)));
        Assert.Empty(new LockoutPlanner().Plan(cfg.UserLockouts, [Kid], DateTime.Now));
        Assert.False(ConfigEditing.TrySetLockout(cfg, "kid", [], 5, out _, breakEveryMinutes: 3));
    }
}

public class AppHoursTests
{
    private const string Game = @"C:\Games\game.exe";
    private static readonly TimeWindow Evening = new([DayOfWeek.Monday], new TimeOnly(18, 0), new TimeOnly(21, 0));

    [Fact]
    public void Program_is_blocked_only_inside_its_hours()
    {
        var cfg = new GuardianConfig();
        cfg.Apps.DefaultDeny = false;
        Assert.True(AppCatalog.TrySetHours(cfg, Game, [Evening], out _));
        var monday19 = new DateTime(2026, 11, 23, 19, 0, 0);
        var monday12 = new DateTime(2026, 11, 23, 12, 0, 0);
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(cfg.Apps, new ProcessInfo(Game, null), monday19).Action);
        Assert.Equal(RuleAction.Allow, AppPolicyEvaluator.Evaluate(cfg.Apps, new ProcessInfo(Game, null), monday12).Action);
        Assert.Single(AppCatalog.HoursOf(cfg, Game));
        Assert.False(AppCatalog.IsLocked(cfg, Game)); // a time limit is not a full lock
        Assert.True(AppCatalog.TrySetHours(cfg, Game, [], out _));
        Assert.Empty(cfg.Apps.Rules);
    }

    [Fact]
    public void Hours_are_per_user()
    {
        var cfg = new GuardianConfig();
        UserScopes.TrySetOwnRules(cfg, "kid", true, out _);
        var view = UserScopes.View(cfg, "kid");
        Assert.True(AppCatalog.TrySetHours(view, Game, [Evening], out _));
        UserScopes.Commit(cfg, view);
        Assert.Single(AppCatalog.HoursOf(UserScopes.Effective(cfg, "kid"), Game));
        Assert.Empty(AppCatalog.HoursOf(cfg, Game));
    }

    [Fact]
    public void Protected_windows_parts_get_no_hours()
    {
        Assert.False(AppCatalog.TrySetHours(new GuardianConfig(), @"C:\Windows\explorer.exe", [Evening], out _));
    }
}

public class WifiTests
{
    [Fact]
    public void Commands_allow_known_networks_and_deny_the_rest()
    {
        var cmds = WifiFilters.ApplyCommands(["Home", "Home", "bad\"name"]);
        Assert.Equal("wlan add filter permission=allow ssid=\"Home\" networktype=infrastructure", cmds[0]);
        Assert.Contains("wlan add filter permission=denyall networktype=infrastructure", cmds);
        Assert.Equal(3, cmds.Count);
        Assert.Contains("wlan delete filter permission=denyall networktype=infrastructure", WifiFilters.RemoveCommands(["Home"]));
    }

    [Fact]
    public void Toggle_and_list_edits()
    {
        var cfg = new GuardianConfig();
        Assert.True(ConfigEditing.TrySetToggle(cfg, "wifi.knownOnly", true, out _));
        Assert.True(cfg.Wifi.KnownOnly);
        Assert.True(ConfigEditing.TryEditWifiList(cfg, "add", "Home", out _));
        Assert.True(ConfigEditing.TryEditWifiList(cfg, "add", "Home", out _));
        Assert.Single(cfg.Wifi.Allowed);
        Assert.False(ConfigEditing.TryEditWifiList(cfg, "add", new string('x', 40), out _));
        Assert.True(ConfigEditing.TryEditWifiList(cfg, "remove", "Home", out _));
        Assert.Empty(cfg.Wifi.Allowed);
    }
}
