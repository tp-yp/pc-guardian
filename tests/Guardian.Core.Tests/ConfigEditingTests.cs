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
}
