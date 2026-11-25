using Guardian.Core.Apps;
using Guardian.Core.Config;
using Guardian.Core.Policy;
using Guardian.Core.Scheduling;

namespace Guardian.Core.Tests;

public class V096Tests
{
    private static GuardianConfig Strict()
    {
        var cfg = new GuardianConfig { AdminPasswordHash = "A", RecoveryPasswordHash = "R" };
        ConfigEditing.TrySetToggle(cfg, "apps.enabled", true, out _);
        ConfigEditing.TrySetToggle(cfg, "network.urlFiltering", true, out _);
        ConfigEditing.TrySetToggle(cfg, "vpn.block", true, out _);
        ConfigEditing.TrySetToggle(cfg, "browser.noPrivate", true, out _);
        cfg.Network.Domains.Add("example.com");
        cfg.UserLockouts.Add(new UserLockoutRule { UserName = "kid", LockedDuring = new WeeklySchedule() });
        UserScopes.TrySetOwnRules(cfg, "kid", true, out _);
        cfg.UserPolicies[0].Apps.Enabled = true;
        cfg.UserPolicies[0].Apps.BlockVideoFiles = true;
        cfg.Cooling.Minutes = 30;
        return cfg;
    }

    [Theory]
    [InlineData("geder.dll", "Geder", true)]
    [InlineData("guardian.dll", "guardian", true)]
    [InlineData("geder.dll", "Minecraft", false)]
    [InlineData("vlc.exe", "Geder", false)]
    [InlineData(null, "Geder", false)]
    public void Own_builds_are_recognized(string? original, string? product, bool expected) =>
        Assert.Equal(expected, SelfIdentity.IsGeder(original, product));

    [Fact]
    public void Release_all_turns_everything_off_and_undo_brings_it_back()
    {
        var cfg = Strict();
        ConfigEditing.ReleaseAll(cfg);
        Assert.NotNull(cfg.ReleasedSnapshot);
        Assert.False(UserScopes.AnyAppBlocking(cfg));
        Assert.False(cfg.Network.Enabled);
        Assert.False(cfg.Network.UrlFiltering);
        Assert.False(cfg.Browsers.NoPrivate);
        Assert.Empty(cfg.UserLockouts);
        Assert.Contains("example.com", cfg.Network.Domains); // lists stay
        Assert.Equal("A", cfg.AdminPasswordHash);

        // Releasing twice keeps the first snapshot.
        ConfigEditing.ReleaseAll(cfg);
        Assert.True(ConfigEditing.TryUndoRelease(cfg));
        Assert.Null(cfg.ReleasedSnapshot);
        Assert.True(cfg.Apps.Enabled && cfg.Network.UrlFiltering && cfg.Vpn.Block && cfg.Browsers.NoPrivate);
        Assert.True(cfg.UserPolicies[0].Apps.BlockVideoFiles);
        Assert.Single(cfg.UserLockouts);
        Assert.Equal("A", cfg.AdminPasswordHash);
        Assert.False(ConfigEditing.TryUndoRelease(cfg));
    }

    [Fact]
    public void Release_waits_for_the_delay_but_undo_does_not()
    {
        var cfg = Strict();
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/release", new Dictionary<string, string> { ["action"] = "release" }));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/release", new Dictionary<string, string> { ["action"] = "undo" }));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/backup/restore", new Dictionary<string, string> { ["config"] = "{}" }));
    }

    [Fact]
    public void Backup_has_no_passwords_and_restore_keeps_them()
    {
        var cfg = Strict();
        cfg.Cooling.Pending.Add(new PendingChange { Id = "x", Path = "/api/settings" });
        var file = ConfigEditing.Export(cfg);
        Assert.DoesNotContain("\"A\"", file);
        Assert.DoesNotContain("\"x\"", file);

        var other = new GuardianConfig { AdminPasswordHash = "B", RecoveryPasswordHash = "S" };
        other.Cooling.Pending.Add(new PendingChange { Id = "y" });
        ConfigEditing.Import(other, file);
        Assert.Equal("B", other.AdminPasswordHash);
        Assert.Equal("S", other.RecoveryPasswordHash);
        Assert.True(other.Apps.Enabled && other.Network.UrlFiltering && other.Browsers.NoPrivate);
        Assert.Single(other.UserPolicies);
        Assert.Equal(30, other.Cooling.Minutes);
        Assert.Equal("y", Assert.Single(other.Cooling.Pending).Id);
        Assert.Throws<InvalidDataException>(() => ConfigEditing.Import(other, "not json"));
    }
}
