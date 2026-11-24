using Guardian.Core.Config;
using Guardian.Core.Network;
using Guardian.Core.Policy;
using Xunit;

namespace Guardian.Core.Tests;

public class VpnTests
{
    [Fact]
    public void Known_vpn_programs_are_recognised_also_when_renamed()
    {
        Assert.True(VpnLists.IsVpnProgram(@"C:\Program Files\NordVPN\NordVPN.exe", null));
        Assert.True(VpnLists.IsVpnProgram(@"C:\Users\kid\Desktop\notes.exe", "psiphon3.exe"));
        Assert.True(VpnLists.IsVpnProgram(@"D:\x\calc.exe", "WireGuard"));
        Assert.False(VpnLists.IsVpnProgram(@"C:\Program Files\Mozilla Firefox\firefox.exe", "firefox.exe"));
    }

    [Fact]
    public void Switch_blocks_vpn_sites_in_dns_for_everyone()
    {
        var c = new GuardianConfig();
        c.Network.Enabled = true;
        Assert.True(UserScopes.DnsAllows(c, "www.nordvpn.com"));
        Assert.True(ConfigEditing.TrySetToggle(c, "vpn.block", true, out _));
        Assert.True(c.Network.Enabled && c.Vpn.Block);
        Assert.False(UserScopes.DnsAllows(c, "www.nordvpn.com"));
        Assert.False(UserScopes.DnsAllows(c, "croxyproxy.com"));
        Assert.True(UserScopes.DnsAllows(c, "www.google.com"));
        UserScopes.TrySetOwnRules(c, "kid", true, out _);
        Assert.Same(c.Vpn, UserScopes.View(c, "kid").Vpn);
        Assert.True(UserScopes.AnyAppBlocking(new GuardianConfig { Apps = new AppPolicy { Enabled = false }, Vpn = new VpnPolicy { Block = true } }));
    }
}
