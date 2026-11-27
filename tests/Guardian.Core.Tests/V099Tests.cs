using Guardian.Core.Config;
using Guardian.Core.Policy;
using Guardian.Core.Scheduling;
using Xunit;

namespace Guardian.Core.Tests;

public class TimedSiteTests
{
    private static readonly DateTime T0 = new(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);
    private static TimedSite Quarter() => new() { Site = "youtube.com", UseMinutes = 15, BlockMinutes = 23 * 60 + 45 };

    [Fact]
    public void Quarter_of_an_hour_a_day()
    {
        var t = Quarter();
        Assert.Equal(TimedPhase.Open, TimedSites.State(t, null, T0, T0).Phase);
        Assert.Equal((TimedPhase.Using, T0.AddMinutes(15)), TimedSites.State(t, T0, T0.AddMinutes(14), T0));
        Assert.Equal((TimedPhase.Resting, T0.AddDays(1)), TimedSites.State(t, T0, T0.AddMinutes(15), T0));
        Assert.Equal(TimedPhase.Open, TimedSites.State(t, T0, T0.AddDays(1), T0).Phase);
        Assert.True(TimedSites.RoundOver(t, T0, T0.AddDays(1)));
        Assert.False(TimedSites.RoundOver(t, T0, T0.AddHours(23)));
    }

    [Fact]
    public void Blocked_hours_win()
    {
        var t = new TimedSite { Site = "news.com", BlockedDuring = new() { Windows = [new([DayOfWeek.Friday], new(8, 0), new(13, 0))] } };
        var fri9 = new DateTime(2026, 11, 27, 9, 0, 0);
        Assert.Equal(TimedPhase.Hours, TimedSites.State(t, null, T0, fri9).Phase);
        Assert.Equal(TimedPhase.Open, TimedSites.State(t, null, T0, fri9.AddHours(5)).Phase);
    }

    [Fact]
    public void Blocked_site_joins_the_block_list_and_beats_its_exceptions()
    {
        var p = new NetworkPolicy { Enabled = true, Domains = ["tiktok.com"], Exceptions = ["youtube.com/watch?v=1"], TimedSites = [Quarter()] };
        var shut = TimedSites.Apply(p, _ => true);
        Assert.False(UrlPolicy.IsUrlAllowed(shut, "www.youtube.com", "/watch?v=1"));
        Assert.False(UrlPolicy.IsHostAllowed(shut, "youtube.com"));
        var open = TimedSites.Apply(p, _ => false);
        Assert.True(UrlPolicy.IsHostAllowed(open, "youtube.com"));
        Assert.Equal(["tiktok.com"], p.Domains); // the saved rules are not changed
    }

    [Fact]
    public void In_an_allow_list_an_open_timed_site_is_allowed()
    {
        var p = new NetworkPolicy { Enabled = true, Mode = UrlMode.Allowlist, Domains = ["school.org", "youtube.com"], TimedSites = [Quarter()] };
        Assert.True(UrlPolicy.IsHostAllowed(TimedSites.Apply(p, _ => false), "youtube.com"));
        Assert.False(UrlPolicy.IsHostAllowed(TimedSites.Apply(p, _ => true), "youtube.com"));
        Assert.True(UrlPolicy.IsHostAllowed(TimedSites.Apply(p, _ => true), "school.org"));
    }

    [Fact]
    public void Per_account_rules_reach_the_filter()
    {
        var cfg = new GuardianConfig { UserPolicies = [new() { UserName = "kid", Network = new() { TimedSites = [Quarter()] } }] };
        var eff = TimedSites.Effective(cfg, (scope, _) => scope == "kid");
        Assert.False(UrlPolicy.IsHostAllowed(UserScopes.Effective(eff, @"PC\kid").Network, "youtube.com"));
        Assert.True(UrlPolicy.IsHostAllowed(UserScopes.Effective(eff, @"PC\mom").Network, "youtube.com"));
        Assert.Empty(cfg.UserPolicies[0].Network.Domains);
    }

    [Fact]
    public void Editing_checks_the_rule_and_weaker_changes_wait()
    {
        var cfg = new GuardianConfig { Cooling = { Minutes = 60 } };
        Assert.False(ConfigEditing.TryEditTimedSite(cfg, "set", new() { Site = "youtube.com", UseMinutes = 15 }, out _)); // no block time
        Assert.False(ConfigEditing.TryEditTimedSite(cfg, "set", new() { Site = "youtube.com" }, out _)); // no rule at all
        Assert.True(ConfigEditing.TryEditTimedSite(cfg, "set", new() { Site = "https://www.YouTube.com/", UseMinutes = 15, BlockMinutes = 1425 }, out _));
        Assert.Equal("www.youtube.com", cfg.Network.TimedSites[0].Site);
        var body = (string use, string block) => new Dictionary<string, string> { ["action"] = "set", ["site"] = "www.youtube.com", ["use"] = use, ["block"] = block, ["windows"] = "[]" };
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/network/timed", body("10", "1430")));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/network/timed", body("30", "1425")));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/network/timed", new Dictionary<string, string> { ["action"] = "remove", ["site"] = "www.youtube.com" }));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/network/timed", new Dictionary<string, string> { ["action"] = "set", ["site"] = "new.com", ["use"] = "5", ["block"] = "60" }));
    }
}
