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
    public void Players_are_found_also_when_renamed()
    {
        Assert.True(VideoFiles.IsPlayer(@"C:\Program Files\VideoLAN\VLC\vlc.exe", null));
        Assert.True(VideoFiles.IsPlayer(@"C:\Users\kid\Downloads\homework.exe", "vlc.exe"));
        Assert.False(VideoFiles.IsPlayer(@"C:\Windows\notepad.exe", "NOTEPAD.EXE"));
    }

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
