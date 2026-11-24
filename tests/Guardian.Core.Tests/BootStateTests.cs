using Guardian.Core.Boot;
using Guardian.Core.Config;

namespace Guardian.Core.Tests;

public class BootStateTests
{
    [Theory]
    [InlineData("""<?xml version='1.0' encoding='utf-8'?><WindowsRE version="2.0"><WinreBCD id="{x}"/><InstallState state="1"/></WindowsRE>""", true)]
    [InlineData("""<WindowsRE version="2.0"><InstallState state="0"/></WindowsRE>""", false)]
    [InlineData("""<WindowsRE/>""", null)]
    [InlineData("not xml", null)]
    [InlineData(null, null)]
    public void Reads_recovery_environment_state(string? xml, bool? expected) =>
        Assert.Equal(expected, BootState.WinReEnabled(xml));

    [Fact]
    public void Lockdown_commands_only_for_what_differs()
    {
        Assert.Empty(BootState.LockdownCommands(false, false, false, 1));
        Assert.Equal(["/deletevalue {current} safeboot", "/set {current} recoveryenabled No"],
            BootState.LockdownCommands(true, false, null, null));
        Assert.Contains("/set {current} bootmenupolicy Standard", BootState.LockdownCommands(false, false, false, 0));
    }

    [Fact]
    public void Lockdown_is_a_switch_and_off_by_default()
    {
        var c = new GuardianConfig();
        Assert.False(c.Boot.Lockdown);
        Assert.True(ConfigEditing.TrySetToggle(c, "boot.lockdown", true, out _));
        Assert.True(c.Boot.Lockdown);
    }

    [Fact]
    public void Lockdown_switched_while_editing_a_user_is_shared()
    {
        var c = new GuardianConfig();
        Policy.UserScopes.TrySetOwnRules(c, "kid", true, out _);
        var view = Policy.UserScopes.View(c, "kid");
        ConfigEditing.TrySetToggle(view, "boot.lockdown", true, out _);
        Policy.UserScopes.Commit(c, view);
        Assert.True(c.Boot.Lockdown);
    }
}
