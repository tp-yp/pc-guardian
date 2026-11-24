using Guardian.Core.Config;
using Guardian.Core.Network;

namespace Guardian.Core.Tests;

public class IspFilterTests
{
    private static readonly List<ChainCert> NetFreeChain =
    [
        new("CN=www.google.com", "CN=NetFree Intermediate, O=NetFree", "AA11"),
        new("CN=NetFree Intermediate, O=NetFree", "CN=NetFree Root CA, O=NetFree", "BB22"),
        new("CN=NetFree Root CA, O=NetFree", "CN=NetFree Root CA, O=NetFree", "CC33"),
    ];
    private static readonly List<ChainCert> PublicChain =
    [
        new("CN=www.google.com", "CN=WR2, O=Google Trust Services", "DD44"),
        new("CN=WR2, O=Google Trust Services", "CN=GTS Root R1, O=Google Trust Services LLC", "EE55"),
    ];

    [Fact]
    public void Recognises_filter_by_name_or_pinned_certificate()
    {
        var p = new IspFilterPolicy();
        Assert.True(IspFilterCheck.IsFilterChain(p, NetFreeChain));
        Assert.False(IspFilterCheck.IsFilterChain(p, PublicChain));
        p.Thumbprints.Add("ee:55");
        Assert.True(IspFilterCheck.IsFilterChain(p, PublicChain));
    }

    [Fact]
    public void A_site_named_like_the_filter_is_not_the_filter()
    {
        // Visiting netfree.link over a normal connection must not count as being filtered.
        List<ChainCert> site = [new("CN=netfree.link", "CN=R11, O=Let's Encrypt", "FF66")];
        Assert.False(IspFilterCheck.IsFilterChain(new IspFilterPolicy(), site));
    }

    [Fact]
    public void Decides_fail_closed()
    {
        var p = new IspFilterPolicy();
        var now = DateTime.Now;
        var filtered = IspFilterCheck.Decide(p, [("a", PublicChain, null), ("b", NetFreeChain, null)], now);
        Assert.Equal(IspFilterStatus.Filtered, filtered.Status);
        Assert.False(filtered.ShouldBlock);
        Assert.Equal("NetFree Root CA", IspFilterCheck.Signer(filtered.Chain));

        var open = IspFilterCheck.Decide(p, [("a", PublicChain, null), ("b", null, "timeout")], now);
        Assert.Equal(IspFilterStatus.NotFiltered, open.Status);
        Assert.True(open.ShouldBlock);

        var none = IspFilterCheck.Decide(p, [("a", null, "no route"), ("b", null, "timeout")], now);
        Assert.Equal(IspFilterStatus.NoNetwork, none.Status);
        Assert.True(none.ShouldBlock);
    }

    [Fact]
    public void Switch_turns_on_the_network_layer_and_lists_are_editable()
    {
        var c = new GuardianConfig();
        c.Network.Enabled = false;
        Assert.True(ConfigEditing.TrySetToggle(c, "isp.required", true, out _));
        Assert.True(c.IspFilter.Required && c.Network.Enabled);
        Assert.True(ConfigEditing.TryEditNetworkList(c, "ispNames", "add", "Etrog Root", out _));
        Assert.Contains("Etrog Root", c.IspFilter.IssuerNames);
    }

    [Fact]
    public void Filter_settings_are_shared_by_every_user_scope()
    {
        var c = new GuardianConfig();
        Assert.True(Guardian.Core.Policy.UserScopes.TrySetOwnRules(c, "kid", true, out _));
        var view = Guardian.Core.Policy.UserScopes.View(c, "kid");
        Assert.True(ConfigEditing.TryEditNetworkList(view, "ispThumbprints", "add", "AB12", out _));
        Assert.Contains("AB12", c.IspFilter.Thumbprints);
        Assert.Same(c.IspFilter, Guardian.Core.Policy.UserScopes.Effective(c, "kid").IspFilter);
    }
}
