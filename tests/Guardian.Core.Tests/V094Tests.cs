using System.Text.Json;
using Guardian.Core.Config;
using Guardian.Core.Policy;
using Guardian.Core.Storage;

namespace Guardian.Core.Tests;

public class V094Tests
{
    [Fact]
    public void Each_site_mode_keeps_its_own_lists()
    {
        var n = new NetworkPolicy { Mode = UrlMode.Blocklist, Domains = ["tiktok.com"], Exceptions = ["tiktok.com/@ok"] };
        ConfigEditing.SetUrlMode(n, UrlMode.Allowlist);
        Assert.Empty(n.Domains);
        Assert.Empty(n.Exceptions);
        n.Domains.Add("wikipedia.org");
        ConfigEditing.SetUrlMode(n, UrlMode.Blocklist);
        Assert.Equal(["tiktok.com"], n.Domains);
        Assert.Equal(["tiktok.com/@ok"], n.Exceptions);
        ConfigEditing.SetUrlMode(n, UrlMode.Allowlist);
        Assert.Equal(["wikipedia.org"], n.Domains);
        ConfigEditing.SetUrlMode(n, UrlMode.Allowlist); // same mode: nothing moves
        Assert.Equal(["wikipedia.org"], n.Domains);
    }

    [Fact]
    public void Old_delay_in_hours_becomes_minutes()
    {
        var cfg = JsonSerializer.Deserialize<GuardianConfig>("""{"cooling":{"hours":3}}""", EncryptedConfigStore.Json)!;
        Assert.Equal(180, cfg.Cooling.Minutes);
        var again = JsonSerializer.Deserialize<GuardianConfig>(JsonSerializer.Serialize(cfg, EncryptedConfigStore.Json), EncryptedConfigStore.Json)!;
        Assert.Equal(180, again.Cooling.Minutes);
        Assert.DoesNotContain("\"hours\"", JsonSerializer.Serialize(cfg.Cooling, EncryptedConfigStore.Json), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("minutes", "1", 1)]
    [InlineData("minutes", "0", 0)]
    [InlineData("hours", "2", 120)]
    public void Delay_accepts_minutes_without_a_minimum(string key, string value, int expected)
    {
        Assert.True(Cooling.TryMinutes(new Dictionary<string, string> { [key] = value }, out var m));
        Assert.Equal(expected, m);
    }

    [Fact]
    public void Shorter_delay_is_weakening_in_minutes()
    {
        var cfg = new GuardianConfig { Cooling = { Minutes = 30 } };
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/cooling/set", new Dictionary<string, string> { ["minutes"] = "29" }));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/cooling/set", new Dictionary<string, string> { ["minutes"] = "45" }));
        Assert.Equal("90 דקות", CoolingPolicy.Describe(90));
        Assert.Equal("שעתיים", CoolingPolicy.Describe(120));
        Assert.Equal("3 שעות", CoolingPolicy.Describe(180));
    }

    [Fact]
    public void Own_rules_copy_the_saved_lists_of_the_other_mode()
    {
        var cfg = new GuardianConfig();
        cfg.Network.OtherDomains.Add("wikipedia.org");
        Assert.True(UserScopes.TrySetOwnRules(cfg, "kid", true, out _));
        Assert.Equal(["wikipedia.org"], cfg.UserPolicies[0].Network.OtherDomains);
    }
}
