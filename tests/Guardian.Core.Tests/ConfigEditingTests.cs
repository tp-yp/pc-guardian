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
