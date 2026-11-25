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

public class AdminToolsTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe", null, true)]
    [InlineData(@"C:\Windows\WinSxS\amd64_x\powershell.exe", null, true)]
    [InlineData(@"C:\Users\kid\Downloads\notes.exe", "Cmd.Exe", true)] // renamed copy
    [InlineData(@"C:\Windows\System32\regedit.exe", null, true)]
    [InlineData(@"C:\Windows\System32\taskmgr.exe", null, false)]
    [InlineData(@"C:\Program Files\App\app.exe", "app.exe", false)]
    public void Recognises_tools_by_name_and_inner_name(string path, string? orig, bool expected) =>
        Assert.Equal(expected, AdminTools.Is(path, orig));

    [Fact]
    public void Switch_is_per_user_and_turns_on_blocking()
    {
        var cfg = new GuardianConfig();
        cfg.Apps.Enabled = false;
        UserScopes.TrySetOwnRules(cfg, "kid", true, out _);
        var view = UserScopes.View(cfg, "kid");
        Assert.True(ConfigEditing.TrySetToggle(view, "apps.blockAdminTools", true, out _));
        UserScopes.Commit(cfg, view);
        Assert.True(UserScopes.Effective(cfg, "kid").Apps.BlockAdminTools);
        Assert.False(cfg.Apps.BlockAdminTools);
        Assert.True(UserScopes.AnyAppBlocking(cfg));
    }
}

public class CoolingTests
{
    private static Dictionary<string, string> B(params string[] kv) =>
        Enumerable.Range(0, kv.Length / 2).ToDictionary(i => kv[2 * i], i => kv[2 * i + 1]);

    [Fact]
    public void Turning_off_weakens_turning_on_does_not()
    {
        var cfg = new GuardianConfig { Cooling = { Hours = 24 } };
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/settings", B("key", "apps.enabled", "value", "false")));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/settings", B("key", "apps.enabled", "value", "true")));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/settings", B("key", "apps.trustProgramFiles", "value", "true")));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/cooling/set", B("hours", "2")));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/cooling/set", B("hours", "48")));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/tools/unlock", B("minutes", "30")));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/tools/unlock", B("minutes", "0")));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/password", B()));
    }

    [Fact]
    public void Lists_and_hours()
    {
        var cfg = new GuardianConfig();
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/network/list", B("list", "domains", "action", "remove", "value", "a.com")));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/network/list", B("list", "domains", "action", "add", "value", "a.com")));
        cfg.Network.Mode = UrlMode.Allowlist;
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/network/list", B("list", "domains", "action", "add", "value", "a.com")));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/catalog/set", B("kind", "lock", "value", "false")));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/catalog/set", B("kind", "lock", "value", "true")));

        const string w1 = """[{"days":["Monday"],"start":"18:00","end":"21:00"}]""";
        const string w2 = """[{"days":["Monday"],"start":"18:00","end":"21:00"},{"days":["Friday"],"start":"10:00","end":"12:00"}]""";
        AppCatalog.TrySetHours(cfg, @"C:\g.exe", System.Text.Json.JsonSerializer.Deserialize<List<TimeWindow>>(w1, Guardian.Core.Storage.EncryptedConfigStore.Json)!, out _);
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/catalog/hours", B("path", @"C:\g.exe", "windows", w2))); // more hours = stricter
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/catalog/hours", B("path", @"C:\g.exe", "windows", "[]")));

        ConfigEditing.TrySetLockout(cfg, "kid", [], 5, out _, breakEveryMinutes: 30);
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/users/set", B("user", "kid", "windows", "[]", "breakEvery", "20")));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/users/set", B("user", "kid", "windows", "[]", "breakEvery", "0")));
    }
}

public class AdminToolsParentTests
{
    [Theory]
    [InlineData(@"C:\Windows\explorer.exe", true)]
    [InlineData(@"C:\Windows\System32\Taskmgr.exe", true)]
    [InlineData(@"C:\Windows\System32\cmd.exe", true)]
    [InlineData(null, true)]
    [InlineData(@"C:\Program Files\Vendor\updater.exe", false)]
    public void Tools_started_by_hand_are_blocked_background_ones_are_not(string? parent, bool byHand) =>
        Assert.Equal(byHand, AdminTools.StartedByHand(parent));
}

public class ThreeModeScheduleTests
{
    private static readonly TimeWindow Evening = new(Enum.GetValues<DayOfWeek>(), new TimeOnly(16, 0), new TimeOnly(22, 0));
    private static readonly TimeWindow Night = new(Enum.GetValues<DayOfWeek>(), new TimeOnly(23, 0), new TimeOnly(6, 0));

    [Fact]
    public void Locked_hours_lock_the_screen_and_only_when_it_is_open()
    {
        var rules = new List<UserLockoutRule> { new() { UserName = "kid", LockedDuring = new() { Windows = [Night] } } };
        var p = new LockoutPlanner();
        var at = new DateTime(2026, 11, 25, 23, 30, 0);
        Assert.Equal(LockoutKind.Lock, Assert.Single(p.Plan(rules, [new UserSession(2, @"PC\kid")], at)).Kind);
        Assert.Empty(p.Plan(rules, [new UserSession(2, @"PC\kid", Unlocked: false)], at));
    }

    [Fact]
    public void Addiction_prevention_only_in_its_hours_and_counts_from_their_start()
    {
        var rule = new UserLockoutRule { UserName = "kid", BreakEveryMinutes = 30, WarningMinutes = 0, BreakDuring = new() { Windows = [Evening] }, LockedDuring = new() { Windows = [Night] } };
        var p = new BreakPlanner();
        var kid = new UserSession(2, @"PC\kid");
        DateTime L(int h, int m) => new(2026, 11, 25, h, m, 0);
        DateTime U(int h, int m) => DateTime.SpecifyKind(L(h, m), DateTimeKind.Utc);
        Assert.Empty(p.Plan([rule], [kid], U(14, 0), local: L(14, 0)));   // normal use since 14:00
        Assert.Empty(p.Plan([rule], [kid], U(15, 59), local: L(15, 59)));
        Assert.Empty(p.Plan([rule], [kid], U(16, 1), local: L(16, 1)));   // count starts at 16:00, not 14:00
        Assert.Equal(LockoutKind.Logoff, Assert.Single(p.Plan([rule], [kid], U(16, 31), local: L(16, 31))).Kind);
        Assert.Empty(p.Plan([rule], [kid], U(23, 40), local: L(23, 40)));  // locked hours: no sign-out (the lock handles it)
    }

    [Fact]
    public void Fewer_addiction_hours_is_weaker()
    {
        var cfg = new GuardianConfig { Cooling = { Hours = 5 } };
        ConfigEditing.TrySetLockout(cfg, "kid", [], 5, out _, 30, [Evening]);
        var b = new Dictionary<string, string> { ["user"] = "kid", ["windows"] = "[]", ["breakEvery"] = "30",
            ["breakWindows"] = """[{"days":["Monday"],"start":"16:00","end":"17:00"}]""" };
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/users/set", b));
        b["breakWindows"] = "[]"; // all day = stricter
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/users/set", b));
    }
}
