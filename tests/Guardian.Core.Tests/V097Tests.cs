using Guardian.Core.Apps;
using Guardian.Core.Config;
using Guardian.Core.Policy;

namespace Guardian.Core.Tests;

public class V097Tests
{
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0);

    [Fact]
    public void Scanned_folder_allows_only_the_programs_found_in_it()
    {
        var cfg = new GuardianConfig { Apps = { Enabled = true, DefaultDeny = true } };
        var n = ConfigEditing.AllowScanned(cfg, @"C:\Tools", [(@"C:\Tools\a.exe", "AA"), (@"C:\Tools\sub\b.exe", "BB")]);
        Assert.Equal(2, n);
        AppDecision Run(string path, string? hash) => AppPolicyEvaluator.Evaluate(cfg.Apps, new(path, hash), Now);
        Assert.Equal(RuleAction.Allow, Run(@"C:\Tools\a.exe", "aa").Action);
        Assert.Equal(RuleAction.Block, Run(@"C:\Tools\a.exe", "CC").Action);        // replaced by another program
        Assert.Equal(RuleAction.Block, Run(@"C:\Tools\new.exe", "AA").Action);      // copied in later
        Assert.Equal(RuleAction.Block, Run(@"C:\Tools\a.exe.exe", "AA").Action);    // look-alike name
        Assert.Equal(Reasons.NotAllowed, Run(@"C:\Tools\new.exe", "AA").Reason);
        // Shown as one entry, removed as one entry, rescanning replaces the list.
        Assert.Equal([@"C:\Tools\"], ConfigEditing.SimpleAppList(cfg, RuleAction.Allow));
        ConfigEditing.AllowScanned(cfg, @"C:\Tools\", [(@"C:\Tools\a.exe", "A2")]);
        Assert.Single(cfg.Apps.Rules);
        Assert.True(ConfigEditing.TryEditAppList(cfg, "allowApps", "remove", @"C:\Tools\", out _));
        Assert.Empty(cfg.Apps.Rules);
    }

    [Fact]
    public void A_rule_for_one_program_is_that_exact_file()
    {
        Assert.True(AppPolicyEvaluator.PathMatches(@"C:\X\game.exe", @"c:\x\GAME.exe"));
        Assert.False(AppPolicyEvaluator.PathMatches(@"C:\X\game.exe.exe", @"C:\X\game.exe"));
        Assert.True(AppPolicyEvaluator.PathMatches(@"C:\X\sub\game.exe", @"C:\X\"));
    }

    [Fact]
    public void Admin_only_folders()
    {
        string[] roots = [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows"];
        Assert.True(ConfigEditing.IsAdminOnlyFolder(@"C:\Program Files\App\a.exe", roots));
        Assert.False(ConfigEditing.IsAdminOnlyFolder(@"C:\Program Files Evil\a.exe", roots));
        Assert.False(ConfigEditing.IsAdminOnlyFolder(@"C:\Users\kid\Downloads\a.exe", roots));
    }

    [Fact]
    public void Windows_apps_are_found_automatically()
    {
        Assert.True(SystemApps.IsPartOfWindows(@"C:\Program Files\WindowsApps\Microsoft.WindowsNotepad_11.2_x64__8wekyb3d8bbwe\Notepad\Notepad.exe"));
        Assert.True(SystemApps.IsPartOfWindows(@"C:\Program Files\Common Files\microsoft shared\ink\TabTip.exe"));
        Assert.False(SystemApps.IsPartOfWindows(@"C:\Program Files\WindowsApps\Roblox_1.0_x64__abc\Roblox.exe"));
        Assert.False(SystemApps.IsPartOfWindows(@"C:\Users\kid\WindowsApps\Microsoft.WindowsX\x.exe"));
    }

    [Fact]
    public void Block_screen_reasons()
    {
        var p = new AppPolicy { Enabled = true, DefaultDeny = false, Rules =
        {
            new AppRule { Action = RuleAction.Block, FileName = "game.exe" },
            new AppRule { Action = RuleAction.Block, FileName = "late.exe", When = new() { Windows = { new([Now.DayOfWeek], new(11, 0), new(13, 0)) } } },
        } };
        Assert.Equal(Reasons.BlockRule, AppPolicyEvaluator.Evaluate(p, new(@"D:\game.exe", null), Now).Reason);
        Assert.Equal(Reasons.BlockedHours, AppPolicyEvaluator.Evaluate(p, new(@"D:\late.exe", null), Now).Reason);
    }

    [Fact]
    public void Other_accounts_rule_follows_anyone_with_program_locking()
    {
        var cfg = new GuardianConfig { Apps = { Enabled = false } };
        Assert.False(UserScopes.AnyProgramLocking(cfg));
        UserScopes.TrySetOwnRules(cfg, "kid", true, out _);
        cfg.UserPolicies[0].Apps.Enabled = true;
        Assert.True(UserScopes.AnyProgramLocking(cfg));
    }

    [Fact]
    public void Catalog_shows_allowed_programs_and_allowing_waits()
    {
        var cfg = new GuardianConfig();
        ConfigEditing.AllowScanned(cfg, @"C:\Tools", [(@"C:\Tools\a.exe", "AA")]);
        Assert.True(AppCatalog.IsAllowed(cfg, @"C:\Tools\a.exe"));
        Assert.False(AppCatalog.IsAllowed(cfg, @"C:\Tools\b.exe"));
        cfg.Cooling.Minutes = 10;
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/catalog/set", new Dictionary<string, string> { ["kind"] = "allow", ["value"] = "true" }));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/catalog/set", new Dictionary<string, string> { ["kind"] = "allow", ["value"] = "false" }));
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/apps/allowRunning", new Dictionary<string, string>()));
    }

    [Fact]
    public void Fresh_install_starts_in_block_list_mode()
    {
        var c = GuardianConfig.CreateInitial("s", "w", "a", "r");
        Assert.False(c.Apps.DefaultDeny);
        Assert.False(c.Apps.TrustProgramFiles);
        Assert.Equal(24, c.ActivityHours);
    }
}

public class SignerRuleTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0);

    [Fact]
    public void Signed_programs_are_allowed_by_publisher_also_after_updates()
    {
        var cfg = new GuardianConfig { Apps = { Enabled = true, DefaultDeny = true } };
        ConfigEditing.AllowScanned(cfg, @"C:\Users\kid\AppData\Local\Discord",
            [(@"C:\Users\kid\AppData\Local\Discord\app-1.0.9\Discord.exe", null, "CN=Discord Inc."),
             (@"C:\Users\kid\AppData\Local\Discord\Update.exe", null, "CN=Discord Inc."),
             (@"C:\Users\kid\AppData\Local\Discord\tool.exe", "AA", null)]);
        Assert.Equal(2, cfg.Apps.Rules.Count); // one rule for the publisher, one for the unsigned file
        AppDecision Run(string path, string? signer, string? hash = null) => AppPolicyEvaluator.Evaluate(cfg.Apps, new(path, hash, null, signer), Now);
        Assert.Equal(RuleAction.Allow, Run(@"C:\Users\kid\AppData\Local\Discord\app-1.0.10\Discord.exe", "CN=Discord Inc.").Action);
        Assert.Equal(RuleAction.Block, Run(@"C:\Users\kid\AppData\Local\Discord\game.exe", null, "BB").Action);
        Assert.Equal(RuleAction.Block, Run(@"C:\Users\kid\AppData\Local\Discord\game.exe", "CN=Game Studio").Action);
        Assert.Equal(RuleAction.Block, Run(@"C:\Users\kid\Downloads\Discord.exe", "CN=Discord Inc.").Action);
        Assert.Equal(RuleAction.Allow, Run(@"C:\Users\kid\AppData\Local\Discord\tool.exe", null, "aa").Action);
        Assert.True(AppCatalog.IsAllowed(cfg, @"C:\Users\kid\AppData\Local\Discord\app-1.0.10\Discord.exe"));
    }
}

public class OfflineTests
{
    [Fact]
    public void No_network_at_all_is_not_blocked_but_failed_checks_are()
    {
        Assert.False(new Guardian.Core.Network.IspFilterResult(Guardian.Core.Network.IspFilterStatus.Offline, DateTime.Now, "", []).ShouldBlock);
        Assert.True(new Guardian.Core.Network.IspFilterResult(Guardian.Core.Network.IspFilterStatus.NoNetwork, DateTime.Now, "", []).ShouldBlock);
        Assert.True(new Guardian.Core.Network.IspFilterResult(Guardian.Core.Network.IspFilterStatus.Unknown, DateTime.Now, "", []).ShouldBlock);
    }
}
