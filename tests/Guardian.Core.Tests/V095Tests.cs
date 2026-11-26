using Guardian.Core.Config;
using Guardian.Core.Policy;

namespace Guardian.Core.Tests;

public class VideoFilesTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\VideoLAN\\VLC\\vlc.exe\" \"C:\\Users\\kid\\Downloads\\movie.mp4\"", true)]
    [InlineData("\"C:\\Program Files\\Google\\Chrome\\chrome.exe\" file:///C:/Users/kid/clip.MKV", true)]
    [InlineData("chrome.exe https://example.com/v.webm?x=1", true)]
    [InlineData("notepad.exe C:\\notes\\list%20of.mp4", true)]
    [InlineData("notepad.exe C:\\notes\\movies.txt", false)]
    [InlineData("code.exe C:\\src\\app.ts", false)]
    [InlineData("explorer.exe", false)]
    [InlineData(null, false)]
    public void Command_line_with_a_video_file(string? cmd, bool expected) => Assert.Equal(expected, VideoFiles.InCommandLine(cmd));

    [Fact]
    public void Switch_is_per_account_and_counts_as_app_blocking()
    {
        var cfg = new GuardianConfig { Apps = { Enabled = false } };
        Assert.True(UserScopes.TrySetOwnRules(cfg, "kid", true, out _));
        var view = UserScopes.View(cfg, "kid");
        Assert.True(ConfigEditing.TrySetToggle(view, "apps.blockVideo", true, out _));
        UserScopes.Commit(cfg, view);
        Assert.True(UserScopes.Effective(cfg, "kid").Apps.BlockVideoFiles);
        Assert.False(cfg.Apps.BlockVideoFiles);
        Assert.True(UserScopes.AnyAppBlocking(cfg));
        Assert.True(Cooling.IsWeakening(cfg, "kid", "/api/settings", new Dictionary<string, string> { ["key"] = "apps.blockVideo", ["value"] = "false" }));
    }

    [Fact]
    public void No_private_browsing_switch()
    {
        var cfg = new GuardianConfig();
        Assert.True(ConfigEditing.TrySetToggle(cfg, "browser.noPrivate", true, out _));
        Assert.True(cfg.Browsers.NoPrivate);
        Assert.True(UserScopes.Effective(cfg, null).Browsers.NoPrivate);
    }
}

public class ProgramSitesTests
{
    private const string App = @"C:\Program Files\School\db.exe";

    [Fact]
    public void Program_with_a_list_reaches_only_its_sites()
    {
        var cfg = new GuardianConfig();
        Assert.True(Guardian.Core.Apps.AppCatalog.TrySetSites(cfg, App, ["school.example.org", " ", "SCHOOL.example.org", "cdn.example.net"], out _));
        Assert.Equal(2, Guardian.Core.Apps.AppCatalog.SitesOf(cfg, App).Count);
        var p = UserScopes.ForProgram(cfg.Network, App.ToUpperInvariant());
        Assert.True(UrlPolicy.IsAllowed(p, "https://school.example.org/login"));
        Assert.True(UrlPolicy.IsAllowed(p, "cdn.example.net"));
        Assert.False(UrlPolicy.IsAllowed(p, "youtube.com"));
        // Other programs keep the account's rules.
        Assert.Same(cfg.Network, UserScopes.ForProgram(cfg.Network, @"C:\other.exe"));
        Assert.Same(cfg.Network, UserScopes.ForProgram(cfg.Network, null));
    }

    [Fact]
    public void Empty_list_removes_the_limit_and_bad_values_are_refused()
    {
        var cfg = new GuardianConfig();
        Guardian.Core.Apps.AppCatalog.TrySetSites(cfg, App, ["a.example.com"], out _);
        Assert.True(Guardian.Core.Apps.AppCatalog.TrySetSites(cfg, App, [], out _));
        Assert.Empty(cfg.Network.AppSites);
        Assert.False(Guardian.Core.Apps.AppCatalog.TrySetSites(cfg, App, ["bad value"], out _));
        Assert.False(Guardian.Core.Apps.AppCatalog.TrySetSites(cfg, @"C:\x.txt", ["a.com"], out _));
    }

    [Fact]
    public void List_is_per_account_and_loosening_waits()
    {
        var cfg = new GuardianConfig();
        UserScopes.TrySetOwnRules(cfg, "kid", true, out _);
        var view = UserScopes.View(cfg, "kid");
        Guardian.Core.Apps.AppCatalog.TrySetSites(view, App, ["a.example.com"], out _);
        UserScopes.Commit(cfg, view);
        Assert.Single(UserScopes.Effective(cfg, "kid").Network.AppSites);
        Assert.Empty(cfg.Network.AppSites);
        Dictionary<string, string> Body(string sites) => new() { ["path"] = App, ["sites"] = sites };
        Assert.False(Cooling.IsWeakening(cfg, "kid", "/api/catalog/sites", Body("[\"a.example.com\"]")));
        Assert.True(Cooling.IsWeakening(cfg, "kid", "/api/catalog/sites", Body("[\"a.example.com\",\"b.com\"]")));
        Assert.True(Cooling.IsWeakening(cfg, "kid", "/api/catalog/sites", Body("[]")));
        Assert.False(Cooling.IsWeakening(cfg, null, "/api/catalog/sites", Body("[\"b.com\"]")));
    }
}
