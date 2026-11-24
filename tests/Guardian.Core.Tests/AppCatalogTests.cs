using Guardian.Core.Apps;
using Guardian.Core.Config;
using Guardian.Core.Policy;

namespace Guardian.Core.Tests;

public class AppCatalogTests
{
    [Fact]
    public void Merges_dedupes_prefers_start_menu_names_and_drops_noise()
    {
        var list = AppCatalog.Merge([
            new("Mozilla Firefox (x64 he)", @"C:\Program Files\Mozilla Firefox\firefox.exe", AppSource.InstalledApps),
            new("Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe", AppSource.StartMenu),
            new("Uninstall Zoom", @"C:\Zoom\uninstall.exe", AppSource.StartMenu),
            new("Zoom", @"C:\Users\me\AppData\Roaming\Zoom\bin\Zoom.exe", AppSource.StartMenu),
            new("Docs", @"C:\docs\readme.txt", AppSource.StartMenu),
        ]);
        Assert.Equal(["Firefox", "Zoom"], list.Select(a => a.Name));
    }

    [Fact]
    public void Switches_change_policy_and_state()
    {
        var cfg = GuardianConfig.CreateInitial("A1", "B2", "h", "h2");
        const string exe = @"C:\Games\game.exe";
        Assert.True(AppCatalog.TrySet(cfg, exe, "lock", true, out _));
        Assert.True(cfg.Apps.Enabled);
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(cfg.Apps, new(exe, null), DateTime.Now).Action);
        Assert.True(AppCatalog.TrySet(cfg, exe, "internet", true, out _));
        Assert.True(cfg.Network.Enabled);

        var e = Assert.Single(AppCatalog.WithState([new("Game", exe, AppSource.StartMenu)], cfg));
        Assert.True(e.Locked);
        Assert.True(e.InternetBlocked);
        Assert.False(e.DirectInternet);

        AppCatalog.TrySet(cfg, exe, "lock", false, out _);
        Assert.False(AppCatalog.IsLocked(cfg, exe));
        Assert.False(AppCatalog.TrySet(cfg, "C:\\x.txt", "lock", true, out _));
    }
}
