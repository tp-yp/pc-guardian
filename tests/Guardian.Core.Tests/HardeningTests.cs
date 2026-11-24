using Guardian.Core.Apps;
using Guardian.Core.Config;
using Guardian.Core.Network;
using Guardian.Core.Policy;
using Xunit;

namespace Guardian.Core.Tests;

public class HardeningTests
{
    [Theory]
    [InlineData(@"C:\Windows\explorer.exe", true)]
    [InlineData(@"C:\Windows\System32\svchost.exe", true)]
    [InlineData(@"C:\Windows\SystemApps\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\StartMenuExperienceHost.exe", true)]
    [InlineData(@"C:\Program Files (x86)\Microsoft\EdgeWebView\Application\131.0\msedgewebview2.exe", true)]
    [InlineData(@"C:\Windows\System32\cmd.exe", false)]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", false)]
    [InlineData(@"C:\Users\kid\Downloads\explorer.exe", false)]
    public void Essential_windows_parts_are_protected(string path, bool expected) =>
        Assert.Equal(expected, ProtectedApps.IsProtected(path));

    [Fact]
    public void Protected_parts_cannot_be_locked_or_cut_off()
    {
        var c = new GuardianConfig();
        c.Apps.Rules.Add(new AppRule { Action = RuleAction.Block, FileName = "explorer.exe" });
        Assert.Equal(RuleAction.Allow, AppPolicyEvaluator.Evaluate(c.Apps, new ProcessInfo(@"C:\Windows\explorer.exe", null), DateTime.Now).Action);
        Assert.False(ConfigEditing.TryEditAppList(c, "blockApps", "add", "svchost.exe", out var err));
        Assert.Equal(ConfigEditing.ProtectedError, err);
        Assert.False(AppCatalog.TrySet(c, @"C:\Windows\explorer.exe", "lock", true, out _));
        Assert.False(ConfigEditing.TryEditNetworkList(c, "blockedApps", "add", @"C:\Windows\System32\svchost.exe", out _));
        c.Network.BlockedAppPaths.Add(@"C:\Windows\System32\svchost.exe");
        Assert.Empty(UserScopes.InternetBlocks(c, []));
        var listed = AppCatalog.WithState([new CatalogApp("File Explorer", @"C:\Windows\explorer.exe", AppSource.StartMenu), new CatalogApp("Game", @"C:\Games\g.exe", AppSource.StartMenu)], c);
        Assert.Single(listed);
    }

    [Fact]
    public void Renaming_a_blocked_program_does_not_get_around_the_block()
    {
        var p = new AppPolicy { DefaultDeny = false };
        p.Rules.Add(new AppRule { Action = RuleAction.Block, FileName = "steam.exe" });
        var renamed = new ProcessInfo(@"C:\Users\kid\Desktop\notes.exe", null, "Steam.exe");
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(p, renamed, DateTime.Now).Action);

        var c = new GuardianConfig();
        Assert.True(AppCatalog.TrySet(c, @"C:\Users\kid\AppData\Local\Discord\app-1.0\Discord.exe", "lock", true, out _, "Discord"));
        var copy = new ProcessInfo(@"D:\stuff\calc.exe", null, "Discord.exe");
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(c.Apps, copy, DateTime.Now).Action);
    }

    [Fact]
    public void Allow_rules_do_not_use_the_name_inside_the_file()
    {
        var p = new AppPolicy { DefaultDeny = true };
        p.Rules.Add(new AppRule { Action = RuleAction.Allow, FileName = "zoom.exe" });
        var other = new ProcessInfo(@"C:\Users\kid\Downloads\game.exe", null, "zoom.exe");
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(p, other, DateTime.Now).Action);
    }

    [Fact]
    public void Provider_choice_decides_which_certificates_count()
    {
        var netfree = new List<ChainCert> { new("CN=www.google.com", "CN=NetFree CA", "AA") };
        var etrog = new List<ChainCert> { new("CN=www.google.com", "CN=NetSpark Root", "BB") };
        var c = new GuardianConfig();
        Assert.True(IspFilterCheck.IsFilterChain(c.IspFilter, netfree) && IspFilterCheck.IsFilterChain(c.IspFilter, etrog));
        Assert.True(ConfigEditing.TrySetToggle(c, "isp.etrog", false, out _));
        Assert.True(IspFilterCheck.IsFilterChain(c.IspFilter, netfree));
        Assert.False(IspFilterCheck.IsFilterChain(c.IspFilter, etrog));
        // A provider name left in the extra list by v0.6 does not bring the provider back.
        c.IspFilter.IssuerNames.Add("NetSpark");
        Assert.False(IspFilterCheck.IsFilterChain(c.IspFilter, etrog));
        Assert.True(ConfigEditing.TrySetToggle(c, "isp.netfree", false, out _));
        Assert.False(IspFilterCheck.IsFilterChain(c.IspFilter, netfree));
    }

    [Fact]
    public void Microsoft_updates_open_in_both_list_modes()
    {
        const string office = @"C:\Program Files\Common Files\microsoft shared\ClickToRun\OfficeClickToRun.exe";
        var c = new GuardianConfig();
        c.Network.Mode = UrlMode.Allowlist;
        ConfigEditing.AllowMicrosoftUpdates(c, office);
        c.Network.Enabled = true;
        Assert.True(UrlPolicy.IsHostAllowed(c.Network, "fe3.delivery.mp.microsoft.com"));
        Assert.Contains(office, c.Network.AllowDirectApps);
        Assert.Equal(0, ConfigEditing.AllowMicrosoftUpdates(c, office)); // nothing left to change

        var b = new GuardianConfig();
        b.Network.Enabled = true;
        b.Network.Domains.Add("microsoft.com");
        ConfigEditing.AllowMicrosoftUpdates(b, office);
        Assert.True(UrlPolicy.IsHostAllowed(b.Network, "download.windowsupdate.com"));
        Assert.True(UrlPolicy.IsHostAllowed(b.Network, "officecdn.microsoft.com"));
        Assert.False(UrlPolicy.IsHostAllowed(b.Network, "www.microsoft.com"));
    }

    [Fact]
    public void Clock_lock_keeps_the_current_time_zone_and_is_shared()
    {
        var c = new GuardianConfig();
        Assert.True(ConfigEditing.TrySetToggle(c, "clock.locked", true, out _));
        Assert.True(c.Clock.Locked);
        Assert.Equal(TimeZoneInfo.Local.Id, c.Clock.TimeZoneId);
        UserScopes.TrySetOwnRules(c, "kid", true, out _);
        Assert.Same(c.Clock, UserScopes.View(c, "kid").Clock);
    }
}
