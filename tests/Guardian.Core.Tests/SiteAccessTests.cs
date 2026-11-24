using Guardian.Core.Config;
using Guardian.Core.Policy;

namespace Guardian.Core.Tests;

public class SiteAccessTests
{
    [Fact]
    public void Normalizes_targets()
    {
        Assert.Equal("www.youtube.com/watch", ConfigEditing.NormalizeTarget("https://www.YouTube.com/watch?v=1"));
        Assert.Equal("site.com", ConfigEditing.NormalizeTarget("site.com/"));
    }

    [Fact]
    public void Blocklist_block_then_allow_one_page()
    {
        var p = new NetworkPolicy();
        ConfigEditing.SetSiteAccess(p, "youtube.com", allow: false);
        Assert.False(UrlPolicy.IsUrlAllowed(p, "www.youtube.com", "/watch"));
        ConfigEditing.SetSiteAccess(p, "https://www.youtube.com/watch?v=abc", allow: true);
        Assert.True(UrlPolicy.IsUrlAllowed(p, "www.youtube.com", "/watch?v=xyz"));
        Assert.False(UrlPolicy.IsUrlAllowed(p, "www.youtube.com", "/shorts/1"));
        Assert.True(UrlPolicy.IsHostAllowed(p, "www.youtube.com")); // DNS must resolve so the page can load
        Assert.False(UrlPolicy.IsHostAllowed(p, "m.youtube.com"));
    }

    [Fact]
    public void Blocklist_allow_whole_domain_removes_entry()
    {
        var p = new NetworkPolicy { Domains = { "tiktok.com" } };
        ConfigEditing.SetSiteAccess(p, "tiktok.com", allow: true);
        Assert.Empty(p.Domains);
        Assert.Empty(p.Exceptions);
        Assert.True(UrlPolicy.IsUrlAllowed(p, "tiktok.com", "/"));
    }

    [Fact]
    public void Allowlist_allow_then_block_one_page()
    {
        var p = new NetworkPolicy { Mode = UrlMode.Allowlist };
        ConfigEditing.SetSiteAccess(p, "wikipedia.org", allow: true);
        Assert.True(UrlPolicy.IsUrlAllowed(p, "he.wikipedia.org", "/wiki/x"));
        ConfigEditing.SetSiteAccess(p, "he.wikipedia.org/wiki/Bad", allow: false);
        Assert.False(UrlPolicy.IsUrlAllowed(p, "he.wikipedia.org", "/wiki/Bad"));
        Assert.True(UrlPolicy.IsUrlAllowed(p, "he.wikipedia.org", "/wiki/Good"));
        ConfigEditing.SetSiteAccess(p, "wikipedia.org", allow: false);
        Assert.False(UrlPolicy.IsUrlAllowed(p, "he.wikipedia.org", "/wiki/Good"));
    }

    [Fact]
    public void Toggling_back_and_forth_leaves_no_contradictions()
    {
        var p = new NetworkPolicy();
        ConfigEditing.SetSiteAccess(p, "a.com", false);
        ConfigEditing.SetSiteAccess(p, "a.com", true);
        ConfigEditing.SetSiteAccess(p, "a.com", false);
        Assert.Equal(["a.com"], p.Domains);
        Assert.Empty(p.Exceptions);
    }
}
