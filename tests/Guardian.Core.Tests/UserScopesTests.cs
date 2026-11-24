using Guardian.Core.Apps;
using Guardian.Core.Config;
using Guardian.Core.Policy;
using Guardian.Core.Storage;
using System.Text.Json;

namespace Guardian.Core.Tests;

public class UserScopesTests
{
    private static GuardianConfig Cfg()
    {
        var c = new GuardianConfig();
        c.Apps.Enabled = true; c.Apps.DefaultDeny = false;
        c.Apps.Rules.Add(new AppRule { Action = RuleAction.Block, FileName = "game.exe" });
        c.Network.Enabled = true; c.Network.UrlFiltering = true;
        c.Network.Domains.Add("tiktok.com");
        c.Network.BlockedAppPaths.Add(@"C:\Apps\chat.exe");
        c.Network.AllowDirectApps.Add(@"C:\Steam\steam.exe");
        return c;
    }

    private static ProcessInfo P(string path) => new(path, null);

    [Fact]
    public void User_without_own_rules_follows_general()
    {
        var c = Cfg();
        Assert.Same(c, UserScopes.Effective(c, "kid"));
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(UserScopes.Effective(c, "kid").Apps, P(@"C:\x\game.exe"), DateTime.Now).Action);
    }

    [Fact]
    public void Own_rules_start_as_a_copy_and_then_differ()
    {
        var c = Cfg();
        Assert.True(UserScopes.TrySetOwnRules(c, "parent", true, out _));
        var view = UserScopes.View(c, @"PC\parent");
        Assert.True(ConfigEditing.TryEditAppList(view, "blockApps", "remove", "game.exe", out _));
        ConfigEditing.SetSiteAccess(view.Network, "tiktok.com", allow: true);
        UserScopes.Commit(c, view);
        c = RoundTrip(c);

        var parent = UserScopes.Effective(c, "parent");
        var kid = UserScopes.Effective(c, "kid");
        Assert.Equal(RuleAction.Allow, AppPolicyEvaluator.Evaluate(parent.Apps, P(@"C:\x\game.exe"), DateTime.Now).Action);
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(kid.Apps, P(@"C:\x\game.exe"), DateTime.Now).Action);
        Assert.True(UrlPolicy.IsUrlAllowed(parent.Network, "www.tiktok.com", "/"));
        Assert.False(UrlPolicy.IsUrlAllowed(kid.Network, "www.tiktok.com", "/"));
    }

    [Fact]
    public void Shared_switches_and_lists_edited_through_a_user_go_to_general()
    {
        var c = Cfg();
        UserScopes.TrySetOwnRules(c, "kid", true, out _);
        var view = UserScopes.View(c, "kid");
        ConfigEditing.TrySetToggle(view, "apps.enabled", false, out _);
        ConfigEditing.TrySetToggle(view, "apps.defaultDeny", true, out _);
        AppCatalog.TrySet(view, @"C:\Epic\epic.exe", "direct", true, out _);
        UserScopes.Commit(c, view);
        c = RoundTrip(c);

        Assert.False(c.Apps.Enabled);
        Assert.False(c.Apps.DefaultDeny);                       // per-user switch stayed with the user
        Assert.True(UserScopes.Effective(c, "kid").Apps.DefaultDeny);
        Assert.Contains(@"C:\Epic\epic.exe", c.Network.AllowDirectApps);
        Assert.Empty(UserScopes.Find(c, "kid")!.Network.AllowDirectApps);
        Assert.False(UserScopes.Effective(c, "kid").Apps.Enabled); // shared switch applies to the user too
    }

    [Fact]
    public void Removing_own_rules_returns_to_general()
    {
        var c = Cfg();
        UserScopes.TrySetOwnRules(c, "kid", true, out _);
        UserScopes.TrySetOwnRules(c, "kid", false, out _);
        Assert.Empty(c.UserPolicies);
        Assert.False(UserScopes.TrySetOwnRules(c, "bad/name", true, out _));
    }

    [Fact]
    public void Dns_only_blocks_what_everyone_blocks()
    {
        var c = Cfg();
        UserScopes.TrySetOwnRules(c, "parent", true, out _);
        Assert.False(UserScopes.DnsAllows(c, "tiktok.com"));
        ConfigEditing.SetSiteAccess(UserScopes.Find(c, "parent")!.Network, "tiktok.com", allow: true);
        Assert.True(UserScopes.DnsAllows(c, "tiktok.com"));
        Assert.False(UserScopes.DnsAllows(c, "dns.google")); // DoH resolvers stay blocked for all
    }

    [Fact]
    public void Internet_blocks_per_account()
    {
        var c = Cfg();
        string[] accounts = ["parent", "kid", "guest2"];
        Assert.Equal([(@"C:\Apps\chat.exe", (List<string>?)null)], UserScopes.InternetBlocks(c, accounts));

        UserScopes.TrySetOwnRules(c, "parent", true, out _);
        UserScopes.Find(c, "parent")!.Network.BlockedAppPaths.Clear();
        UserScopes.Find(c, "parent")!.Network.BlockedAppPaths.Add(@"C:\Apps\mail.exe");
        var blocks = UserScopes.InternetBlocks(c, accounts).ToDictionary(b => b.Path, b => b.Users);
        Assert.Equal(["kid", "guest2"], blocks[@"C:\Apps\chat.exe"]);
        Assert.Equal(["parent"], blocks[@"C:\Apps\mail.exe"]);

        UserScopes.Find(c, "parent")!.Network.BlockedAppPaths.Add(@"C:\Apps\chat.exe");
        Assert.Null(UserScopes.InternetBlocks(c, accounts).Single(b => b.Path == @"C:\Apps\chat.exe").Users);
    }

    private static GuardianConfig RoundTrip(GuardianConfig c) =>
        JsonSerializer.Deserialize<GuardianConfig>(JsonSerializer.Serialize(c, EncryptedConfigStore.Json), EncryptedConfigStore.Json)!;
}
