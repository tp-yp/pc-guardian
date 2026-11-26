using Guardian.Core.Config;
using Guardian.Core.Security;

namespace Guardian.Core.Tests;

public class ConfigEditingTests
{
    private static GuardianConfig Cfg() => new()
    {
        ServiceName = "SysSync1234",
        AdminPasswordHash = PasswordHasher.Hash("adminpass", 1000),
        RecoveryPasswordHash = PasswordHasher.Hash("rescuepass", 1000),
        Network = { Domains = { "a.com" } },
    };

    [Fact]
    public void Export_strips_hashes()
    {
        var json = ConfigEditing.Export(Cfg());
        Assert.DoesNotContain("pbkdf2", json);
        Assert.Contains("a.com", json);
    }

    [Fact]
    public void Import_keeps_secrets_and_name()
    {
        var cfg = Cfg();
        var edited = Cfg();
        edited.ServiceName = "Hacked";
        edited.AdminPasswordHash = PasswordHasher.Hash("other", 1000);
        edited.Network.Domains = ["b.com"];
        edited.Apps.DefaultDeny = false;
        ConfigEditing.Import(cfg, ConfigEditing.Export(edited).Replace("\"adminPasswordHash\": \"\"", "\"AdminPasswordHash\": \"x\""));
        Assert.Equal("SysSync1234", cfg.ServiceName);
        Assert.True(PasswordHasher.Verify("adminpass", cfg.AdminPasswordHash));
        Assert.Equal(["b.com"], cfg.Network.Domains);
        Assert.False(cfg.Apps.DefaultDeny);
    }

    [Fact]
    public void Import_rejects_invalid_rules()
    {
        var bad = Cfg();
        bad.Apps.Rules.Add(new AppRule());
        Assert.Throws<InvalidDataException>(() => ConfigEditing.Import(Cfg(), ConfigEditing.Export(bad)));
    }

    [Fact]
    public void Fresh_install_has_every_blocking_feature_off()
    {
        var c = GuardianConfig.CreateInitial("A1", "B2", "h1", "h2");
        Assert.False(c.Apps.Enabled);
        Assert.False(c.Network.Enabled);
        Assert.False(c.Network.UrlFiltering);
        Assert.Empty(c.UserLockouts);
        Assert.False(c.Apps.DefaultDeny); // block-list mode: turning program locking on blocks only what is locked
    }

    [Fact]
    public void Toggles_keep_network_and_proxy_consistent()
    {
        var c = GuardianConfig.CreateInitial("A1", "B2", "h1", "h2");
        Assert.True(ConfigEditing.TrySetToggle(c, "network.urlFiltering", true, out _));
        Assert.True(c.Network.Enabled);
        Assert.True(ConfigEditing.TrySetToggle(c, "network.enabled", false, out _));
        Assert.False(c.Network.UrlFiltering);
        Assert.False(ConfigEditing.TrySetToggle(c, "nope", true, out _));
    }

    [Fact]
    public void Allowing_a_blocked_exe_from_the_activity_list_unblocks_it()
    {
        var c = Cfg();
        c.Apps.Enabled = true;
        const string exe = @"C:\Users\me\Downloads\tool.exe";
        var now = new DateTime(2026, 11, 24, 10, 0, 0);
        Assert.Equal(RuleAction.Block, Guardian.Core.Policy.AppPolicyEvaluator.Evaluate(c.Apps, new(exe, null), now).Action);
        Assert.True(ConfigEditing.TryEditAppList(c, "allowApps", "add", exe, out _));
        Assert.Equal(RuleAction.Allow, Guardian.Core.Policy.AppPolicyEvaluator.Evaluate(c.Apps, new(exe, null), now).Action);
    }

    [Fact]
    public void Allowing_an_app_folder_covers_its_helpers_and_new_versions()
    {
        const string exe = @"C:\Users\me\AppData\Local\Discord\app-1.0.9\Discord.exe";
        Assert.Equal(@"C:\Users\me\AppData\Local\Discord\", ConfigEditing.AppFolderOf(exe));
        Assert.Equal(@"D:\Tools\", ConfigEditing.AppFolderOf(@"D:\Tools\x.exe"));
        Assert.Equal(@"C:\Apps\Zoom\", ConfigEditing.AppFolderOf(@"C:\Apps\Zoom\bin\Zoom.exe"));

        var c = Cfg();
        c.Apps.Enabled = true;
        ConfigEditing.TryEditAppList(c, "allowApps", "add", ConfigEditing.AppFolderOf(exe), out _);
        var now = new DateTime(2026, 11, 24, 10, 0, 0);
        Assert.Equal(RuleAction.Allow, Guardian.Core.Policy.AppPolicyEvaluator.Evaluate(c.Apps,
            new(@"C:\Users\me\AppData\Local\Discord\Update.exe", null), now).Action);
        Assert.Equal(RuleAction.Allow, Guardian.Core.Policy.AppPolicyEvaluator.Evaluate(c.Apps,
            new(@"C:\Users\me\AppData\Local\Discord\app-1.0.10\Discord.exe", null), now).Action);
    }

    [Fact]
    public void App_list_editing()
    {
        var c = Cfg();
        Assert.True(ConfigEditing.TryEditAppList(c, "allowApps", "add", @"C:\Users\me\AppData\Local\Discord\", out _));
        Assert.True(ConfigEditing.TryEditAppList(c, "blockApps", "add", "steam.exe", out _));
        Assert.Equal([@"C:\Users\me\AppData\Local\Discord\"], ConfigEditing.SimpleAppList(c, RuleAction.Allow));
        Assert.Equal(["steam.exe"], ConfigEditing.SimpleAppList(c, RuleAction.Block));
        Assert.Equal("steam.exe", c.Apps.Rules[1].FileName);
        Assert.True(ConfigEditing.TryEditAppList(c, "blockApps", "remove", "STEAM.exe", out _));
        Assert.Empty(ConfigEditing.SimpleAppList(c, RuleAction.Block));
    }

    [Fact]
    public void Network_list_editing()
    {
        var cfg = Cfg();
        Assert.True(ConfigEditing.TryEditNetworkList(cfg, "domains", "add", " youtube.com/shorts ", out _));
        Assert.True(ConfigEditing.TryEditNetworkList(cfg, "domains", "add", "YOUTUBE.com/shorts", out _)); // no duplicate
        Assert.Equal(["a.com", "youtube.com/shorts"], cfg.Network.Domains);
        Assert.True(ConfigEditing.TryEditNetworkList(cfg, "domains", "remove", "a.com", out _));
        Assert.False(ConfigEditing.TryEditNetworkList(cfg, "domains", "add", "bad host", out _));
        Assert.False(ConfigEditing.TryEditNetworkList(cfg, "nope", "add", "x.com", out _));
        Assert.True(ConfigEditing.TryEditNetworkList(cfg, "blockedApps", "add", @"C:\Games\game.exe", out _));
    }

    [Fact]
    public void Admin_cannot_change_recovery_password()
    {
        var cfg = Cfg();
        Assert.False(ConfigEditing.TrySetPassword(cfg, AuthResult.Admin, "recovery", "newrescue1", out _));
        Assert.True(ConfigEditing.TrySetPassword(cfg, AuthResult.Recovery, "recovery", "newrescue1", out _));
        Assert.True(PasswordHasher.Verify("newrescue1", cfg.RecoveryPasswordHash));
        Assert.True(ConfigEditing.TrySetPassword(cfg, AuthResult.Admin, "admin", "newadmin1", out _));
        Assert.False(ConfigEditing.TrySetPassword(cfg, AuthResult.Admin, "admin", "newrescue1", out _)); // same as recovery
        Assert.False(ConfigEditing.TrySetPassword(cfg, AuthResult.Admin, "admin", "short", out _));
    }

    private const string ReadmeExample = """
{
  "userLockouts": [
    { "userName": "Kids", "lockedDuring": { "windows": [
      { "days": ["Sunday","Monday","Tuesday","Wednesday","Thursday"], "start": "22:00", "end": "07:00" } ] },
      "warningMinutes": 5 }
  ],
  "apps": {
    "enabled": true, "defaultDeny": true,
    "rules": [
      { "action": "Allow", "pathPrefix": "C:\\Program Files\\Mozilla Firefox\\" },
      { "action": "Block", "fileName": "steam.exe",
        "when": { "windows": [ { "days": ["Monday"], "start": "08:00", "end": "16:00" } ] } }
    ]
  },
  "network": { "enabled": true, "mode": "Blocklist", "domains": ["example.com"], "blockedAppPaths": [] }
}
""";

    [Fact]
    public void Readme_example_imports()
    {
        var cfg = Cfg();
        ConfigEditing.Import(cfg, ReadmeExample);
        Assert.Equal("Kids", cfg.UserLockouts[0].UserName);
        Assert.True(cfg.UserLockouts[0].LockedDuring.IsActive(new DateTime(2026, 11, 24, 6, 0, 0))); // Tue 06:00
        Assert.Equal(@"C:\Program Files\Mozilla Firefox\", cfg.Apps.Rules[0].PathPrefix);
        Assert.Equal(RuleAction.Block, cfg.Apps.Rules[1].Action);
    }
}
