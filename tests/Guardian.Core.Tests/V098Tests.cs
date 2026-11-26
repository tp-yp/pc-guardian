using Guardian.Core.Config;
using Guardian.Core.Lockout;
using Guardian.Core.Policy;
using Xunit;

namespace Guardian.Core.Tests;

public class NewAccountsTests
{
    private static readonly LocalAccount Owner = new(1001, "owner", true, true);
    private static readonly LocalAccount Kid = new(1002, "kid", true, false);
    private static readonly LocalAccount BuiltIn = new(500, "Administrator", false, true);

    [Fact]
    public void Snapshot_knows_accounts_that_are_on_and_accounts_off_for_their_locked_hours()
    {
        var kidLocked = Kid with { Enabled = false };
        var known = NewAccounts.Snapshot([Owner, kidLocked, BuiltIn], [@"PC\kid"]);
        Assert.Equal([1001, 1002], known.Order());
    }

    [Fact]
    public void A_new_administrator_is_turned_off()
    {
        var fresh = new LocalAccount(1003, "second", true, true);
        var (off, on) = NewAccounts.Plan(true, [Owner, Kid, fresh], new HashSet<int> { 1001, 1002 }, new HashSet<int>());
        Assert.Equal(["second"], off.Select(a => a.Name));
        Assert.Empty(on);
    }

    [Fact]
    public void A_built_in_account_turned_on_later_is_new_too()
    {
        var (off, _) = NewAccounts.Plan(true, [Owner, BuiltIn with { Enabled = true }], new HashSet<int> { 1001 }, new HashSet<int>());
        Assert.Equal([500], off.Select(a => a.Rid));
    }

    [Fact]
    public void The_last_administrator_is_never_turned_off()
    {
        var fresh = new LocalAccount(1003, "second", true, true);
        var ownerOff = Owner with { Enabled = false };
        var (off, _) = NewAccounts.Plan(true, [ownerOff, Kid, fresh], new HashSet<int> { 1001, 1002 }, new HashSet<int>());
        Assert.Empty(off);
    }

    [Fact]
    public void Approved_accounts_come_back_on_and_turning_off_the_switch_brings_back_all()
    {
        var waiting = new LocalAccount(1003, "second", false, false);
        var (off, on) = NewAccounts.Plan(true, [Owner, waiting], new HashSet<int> { 1001, 1003 }, new HashSet<int> { 1003 });
        Assert.Empty(off);
        Assert.Equal([1003], on.Select(a => a.Rid));

        (off, on) = NewAccounts.Plan(false, [Owner, waiting], new HashSet<int>(), new HashSet<int> { 1003 });
        Assert.Empty(off);
        Assert.Equal([1003], on.Select(a => a.Rid));
    }

    [Fact]
    public void An_account_turned_on_again_by_hand_is_turned_off_again()
    {
        var back = new LocalAccount(1003, "second", true, false);
        var (off, _) = NewAccounts.Plan(true, [Owner, back], new HashSet<int> { 1001 }, new HashSet<int> { 1003 });
        Assert.Equal([1003], off.Select(a => a.Rid));
    }

    [Fact]
    public void Switch_is_off_on_a_fresh_install_and_released_with_everything_else()
    {
        var cfg = GuardianConfig.CreateInitial("a", "b", "h", "r");
        Assert.False(cfg.Accounts.BlockNew);
        Assert.True(ConfigEditing.TrySetToggle(cfg, "accounts.blockNew", true, out _));
        Assert.True(cfg.Accounts.BlockNew);
        ConfigEditing.ReleaseAll(cfg);
        Assert.False(cfg.Accounts.BlockNew);
        Assert.True(ConfigEditing.TryUndoRelease(cfg));
        Assert.True(cfg.Accounts.BlockNew);
    }

    [Fact]
    public void Turning_off_and_approving_wait_for_the_delay()
    {
        var cfg = new GuardianConfig { Cooling = { Minutes = 60 }, Accounts = { BlockNew = true } };
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/settings", new Dictionary<string, string> { ["key"] = "accounts.blockNew", ["value"] = "false" }));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/settings", new Dictionary<string, string> { ["key"] = "accounts.blockNew", ["value"] = "true" }));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/accounts/approve", new Dictionary<string, string> { ["rid"] = "1003" }));
    }
}

public class V098Tests
{
    [Fact]
    public void Disable_account_is_kept_released_with_everything_and_turning_on_waits()
    {
        var cfg = new GuardianConfig { Cooling = { Minutes = 60 } };
        Assert.True(ConfigEditing.TrySetAccountDisabled(cfg, "kid", true, out _));
        Assert.True(ConfigEditing.TrySetAccountDisabled(cfg, "KID", true, out _));
        Assert.Equal(["KID"], cfg.Accounts.Disabled);
        Assert.False(ConfigEditing.TrySetAccountDisabled(cfg, "a/b", true, out _));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/users/disable", new Dictionary<string, string> { ["user"] = "kid", ["value"] = "false" }));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/users/disable", new Dictionary<string, string> { ["user"] = "kid", ["value"] = "true" }));
        ConfigEditing.ReleaseAll(cfg);
        Assert.Empty(cfg.Accounts.Disabled);
        Assert.True(ConfigEditing.TryUndoRelease(cfg));
        Assert.Single(cfg.Accounts.Disabled);
        Assert.True(ConfigEditing.TrySetAccountDisabled(cfg, "kid", false, out _));
        Assert.Empty(cfg.Accounts.Disabled);
    }

    [Fact]
    public void Reset_this_PC_inside_Settings_is_an_admin_tool()
    {
        Assert.True(AdminTools.Is(@"C:\Windows\ImmersiveControlPanel\SystemSettingsAdminFlows.exe", null));
        Assert.True(AdminTools.StartedByHand(@"C:\Windows\ImmersiveControlPanel\SystemSettings.exe"));
    }

    [Fact]
    public void Video_block_closes_programs_opened_with_a_video_file_only()
    {
        Assert.True(VideoFiles.InCommandLine("\"C:\\Program Files\\VideoLAN\\VLC\\vlc.exe\" \"D:\\movie.mkv\""));
        Assert.False(VideoFiles.InCommandLine("\"C:\\Program Files\\VideoLAN\\VLC\\vlc.exe\""));
    }
}
