using Guardian.Core.Apps;
using Guardian.Core.Config;
using Guardian.Core.Network;
using Guardian.Core.Policy;

namespace Guardian.Core.Tests;

public class LocalNetworkTests
{
    [Theory]
    [InlineData("192.168.1.20", "192.168.1.20")]
    [InlineData(" 192.168.1.77/24 ", "192.168.1.0/24")]
    [InlineData("10.0.0.5", "10.0.0.5")]
    [InlineData("172.20.1.1/16", "172.20.0.0/16")]
    [InlineData("fe80::1", "fe80::1")]
    public void Accepts_home_network_addresses(string input, string expected)
    {
        Assert.True(LocalNetwork.TryNormalize(input, out var n, out _));
        Assert.Equal(expected, n);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("0.0.0.0/0")]
    [InlineData("192.168.0.0/8")]      // reaches outside 192.168/16
    [InlineData("172.32.0.1")]
    [InlineData("nas.local")]
    [InlineData("192.168.1.1/33")]
    [InlineData("2001:4860::8888")]
    [InlineData("")]
    public void Refuses_internet_addresses_and_junk(string input) =>
        Assert.False(LocalNetwork.TryNormalize(input, out _, out _));

    [Fact]
    public void Contains_matches_addresses_not_names()
    {
        var list = new[] { "192.168.1.0/24", "10.0.0.7" };
        Assert.True(LocalNetwork.Contains(list, "192.168.1.200"));
        Assert.True(LocalNetwork.Contains(list, "10.0.0.7"));
        Assert.False(LocalNetwork.Contains(list, "10.0.0.8"));
        Assert.False(LocalNetwork.Contains(list, "192.168.2.1"));
        Assert.False(LocalNetwork.Contains(list, "192.168.1.200.evil.com"));
        Assert.False(LocalNetwork.Contains(["8.8.8.8"], "8.8.8.8")); // a bad entry never counts
    }

    [Fact]
    public void Subtract_leaves_holes_for_the_entries()
    {
        var r = LocalNetwork.Subtract("0.0.0.0-126.255.255.255,128.0.0.0-255.255.255.255", ["192.168.1.0/24", "10.0.0.7"]);
        Assert.Equal("0.0.0.0-10.0.0.6,10.0.0.8-126.255.255.255,128.0.0.0-192.168.0.255,192.168.2.0-255.255.255.255", r);
        Assert.Equal("1.0.0.0-2.0.0.0", LocalNetwork.Subtract("1.0.0.0-2.0.0.0", []));
        Assert.Equal("192.168.1.0-192.168.1.255,10.0.0.7", LocalNetwork.FirewallList(["192.168.1.0/24", "10.0.0.7", "8.8.8.8"]));
    }

    [Fact]
    public void Subtract_handles_ipv6()
    {
        var r = LocalNetwork.Subtract("::2-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", ["fe80::/10"]);
        Assert.Equal("::2-fe7f:ffff:ffff:ffff:ffff:ffff:ffff:ffff,fec0::-ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", r);
    }

    [Fact]
    public void Exempt_addresses_pass_the_site_filter_even_in_allowlist_mode()
    {
        var p = new NetworkPolicy { Mode = UrlMode.Allowlist, Domains = ["example.com"], LocalAddresses = ["192.168.1.0/24"] };
        Assert.True(UrlPolicy.IsUrlAllowed(p, "192.168.1.5", "/admin"));
        Assert.True(UrlPolicy.IsHostAllowed(p, "192.168.1.5"));
        Assert.False(UrlPolicy.IsUrlAllowed(p, "192.168.3.5", "/"));
    }

    [Fact]
    public void Editing_the_list_normalizes_refuses_and_counts_as_weakening()
    {
        var cfg = new GuardianConfig();
        Assert.True(ConfigEditing.TryEditNetworkList(cfg, "localAddresses", "add", "192.168.1.9/24", out _));
        Assert.Equal(["192.168.1.0/24"], cfg.Network.LocalAddresses);
        Assert.False(ConfigEditing.TryEditNetworkList(cfg, "localAddresses", "add", "1.1.1.1", out var err));
        Assert.Equal(LocalNetwork.Error, err);
        Assert.True(Cooling.IsWeakening(cfg, null, "/api/network/list", new Dictionary<string, string> { ["list"] = "localAddresses", ["action"] = "add", ["value"] = "10.0.0.1" }));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/network/list", new Dictionary<string, string> { ["list"] = "localAddresses", ["action"] = "remove", ["value"] = "192.168.1.0/24" }));
    }

    [Fact]
    public void List_is_shared_by_all_accounts()
    {
        var cfg = new GuardianConfig();
        Assert.True(UserScopes.TrySetOwnRules(cfg, "kid", true, out _));
        var view = UserScopes.View(cfg, "kid");
        Assert.True(ConfigEditing.TryEditNetworkList(view, "localAddresses", "add", "192.168.1.20", out _));
        UserScopes.Commit(cfg, view);
        Assert.Equal(["192.168.1.20"], cfg.Network.LocalAddresses);
        Assert.Equal(["192.168.1.20"], UserScopes.Effective(cfg, "kid").Network.LocalAddresses);
    }
}

public class UserProgramsTests
{
    private static readonly Dictionary<string, string> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"C:\Users\kid"] = @"PC\kid",
        [@"C:\Users\kid2"] = @"PC\kid2",
        [@"C:\Users\parent"] = @"PC\parent",
    };

    [Fact]
    public void Owner_is_found_by_profile_folder()
    {
        Assert.Equal(@"PC\kid", UserPrograms.OwnerOf(@"C:\Users\kid\AppData\Local\Programs\Game\game.exe", Profiles));
        Assert.Equal(@"PC\kid2", UserPrograms.OwnerOf(@"c:\users\KID2\Downloads\x.exe", Profiles));
        Assert.Null(UserPrograms.OwnerOf(@"C:\Program Files\Zoom\zoom.exe", Profiles));
        Assert.Null(UserPrograms.OwnerOf(@"C:\Users\Public\Desktop\x.exe", Profiles));
    }

    [Fact]
    public void Other_accounts_programs_are_foreign()
    {
        Assert.True(UserPrograms.IsOtherUsers(@"C:\Users\parent\Downloads\tool.exe", "kid", Profiles));
        Assert.False(UserPrograms.IsOtherUsers(@"C:\Users\kid\Downloads\tool.exe", @"PC\kid", Profiles));
        Assert.False(UserPrograms.IsOtherUsers(@"C:\Program Files\x.exe", "kid", Profiles));
        Assert.False(UserPrograms.IsOtherUsers(@"C:\Users\parent\x.exe", null, Profiles)); // system processes
    }

    [Fact]
    public void Account_screen_lists_shared_and_own_programs_only()
    {
        CatalogEntry E(string path) => new("n", path, AppSource.StartMenu, false, false, false);
        var all = new[] { E(@"C:\Program Files\a.exe"), E(@"C:\Users\kid\AppData\b.exe"), E(@"C:\Users\parent\AppData\c.exe") };
        var kid = UserPrograms.ForUser(all, "kid", Profiles);
        Assert.Equal([@"C:\Program Files\a.exe", @"C:\Users\kid\AppData\b.exe"], kid.Select(e => e.ExePath));
        Assert.Equal([null, "kid"], kid.Select(e => e.Owner));
        var general = UserPrograms.ForUser(all, null, Profiles);
        Assert.Equal(3, general.Count);
        Assert.Equal("parent", general[2].Owner);
    }
}
