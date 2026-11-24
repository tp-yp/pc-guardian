using System.Security.Cryptography;
using Guardian.Core.Config;
using Guardian.Core.Naming;
using Guardian.Core.Policy;
using Guardian.Core.Scheduling;
using Guardian.Core.Security;
using Guardian.Core.Storage;

namespace Guardian.Core.Tests;

/// <summary>Test-only protector (XOR) standing in for DPAPI.</summary>
internal sealed class FakeProtector : IKeyProtector
{
    public byte[] Protect(byte[] d) => d.Select(b => (byte)(b ^ 0x5A)).ToArray();
    public byte[] Unprotect(byte[] d) => Protect(d);
}

public class PasswordTests
{
    [Fact]
    public void Verifies_correct_and_rejects_wrong()
    {
        var h = PasswordHasher.Hash("s3cret", iterations: 1000);
        Assert.True(PasswordHasher.Verify("s3cret", h));
        Assert.False(PasswordHasher.Verify("S3cret", h));
        Assert.False(PasswordHasher.Verify("s3cret", "garbage"));
        Assert.False(PasswordHasher.Verify("", h));
    }

    [Fact]
    public void Same_password_gets_different_salt()
        => Assert.NotEqual(PasswordHasher.Hash("x", 1000), PasswordHasher.Hash("x", 1000));
}

public class AdminGateTests
{
    private static GuardianConfig Cfg() => new()
    {
        AdminPasswordHash = PasswordHasher.Hash("admin", 1000),
        RecoveryPasswordHash = PasswordHasher.Hash("rescue", 1000),
    };

    [Fact]
    public void Distinguishes_admin_and_recovery()
    {
        var g = new AdminGate();
        Assert.Equal(AuthResult.Admin, g.Authenticate(Cfg(), "admin"));
        Assert.Equal(AuthResult.Recovery, g.Authenticate(Cfg(), "rescue"));
        Assert.Equal(AuthResult.Denied, g.Authenticate(Cfg(), "nope"));
    }

    [Fact]
    public void Locks_out_after_repeated_failures_then_recovers()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0);
        var g = new AdminGate(() => now) { MaxFailures = 3, LockoutDuration = TimeSpan.FromMinutes(10) };
        var cfg = Cfg();
        g.Authenticate(cfg, "a"); g.Authenticate(cfg, "b");
        Assert.Equal(AuthResult.LockedOut, g.Authenticate(cfg, "c"));
        Assert.Equal(AuthResult.LockedOut, g.Authenticate(cfg, "admin"));
        now = now.AddMinutes(11);
        Assert.Equal(AuthResult.Admin, g.Authenticate(cfg, "admin"));
    }
}

public class StoreTests
{
    private static string TempDir() => Directory.CreateTempSubdirectory("guardian-test").FullName;

    [Fact]
    public void Save_recreates_a_folder_removed_after_the_store_was_opened()
    {
        // An update uninstalls first, which deletes the data folder, then saves the kept settings.
        var dir = TempDir();
        var store = new EncryptedConfigStore(dir, new FakeProtector());
        Directory.Delete(dir, true);
        store.Save(new GuardianConfig { ServiceName = "SysSyncNet1234" });
        Assert.Equal("SysSyncNet1234", store.Load().ServiceName);
    }

    [Fact]
    public void Roundtrips_and_is_not_plaintext()
    {
        var dir = TempDir();
        var store = new EncryptedConfigStore(dir, new FakeProtector());
        store.Save(new GuardianConfig { ServiceName = "SysSyncNet1234", Network = { Domains = { "example.com" } } });
        var raw = File.ReadAllText(Path.Combine(dir, "config.bin"));
        Assert.DoesNotContain("example.com", raw);
        var back = new EncryptedConfigStore(dir, new FakeProtector()).Load();
        Assert.Equal("SysSyncNet1234", back.ServiceName);
        Assert.Equal(["example.com"], back.Network.Domains);
    }

    [Fact]
    public void Tampering_is_detected()
    {
        var dir = TempDir();
        var store = new EncryptedConfigStore(dir, new FakeProtector());
        store.Save(new GuardianConfig());
        var path = Path.Combine(dir, "config.bin");
        var bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 1;
        File.WriteAllBytes(path, bytes);
        Assert.ThrowsAny<CryptographicException>(() => store.Load());
    }

    [Fact]
    public void Update_requires_password()
    {
        var store = new EncryptedConfigStore(TempDir(), new FakeProtector());
        store.Save(new GuardianConfig { AdminPasswordHash = PasswordHasher.Hash("admin", 1000) });
        var gate = new AdminGate();
        Assert.Equal(AuthResult.Denied, store.Update(gate, "wrong", c => c.Apps.DefaultDeny = false));
        Assert.True(store.Load().Apps.DefaultDeny);
        Assert.Equal(AuthResult.Admin, store.Update(gate, "admin", c => c.Apps.DefaultDeny = false));
        Assert.False(store.Load().Apps.DefaultDeny);
    }
}

public class ScheduleTests
{
    private static readonly DateTime Mon = new(2026, 11, 23); // Monday

    [Fact]
    public void Empty_schedule_is_always_active() => Assert.True(WeeklySchedule.Always.IsActive(Mon));

    [Fact]
    public void Daytime_window()
    {
        var w = new TimeWindow([DayOfWeek.Monday], new(9, 0), new(17, 0));
        Assert.True(w.Contains(Mon.AddHours(9)));
        Assert.False(w.Contains(Mon.AddHours(17)));
        Assert.False(w.Contains(Mon.AddDays(1).AddHours(10)));
    }

    [Fact]
    public void Overnight_window_spills_into_next_day()
    {
        var w = new TimeWindow([DayOfWeek.Monday], new(22, 0), new(7, 0));
        Assert.True(w.Contains(Mon.AddHours(23)));
        Assert.True(w.Contains(Mon.AddDays(1).AddHours(6)));   // Tue 06:00
        Assert.False(w.Contains(Mon.AddHours(6)));             // Mon 06:00 belongs to Sunday's window
        Assert.False(w.Contains(Mon.AddDays(1).AddHours(8)));
    }
}

public class AppPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 23, 12, 0, 0);

    [Fact]
    public void Unknown_exe_blocked_by_default_but_windows_allowed()
    {
        var p = new AppPolicy();
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(p, new(@"D:\portable\game.exe", null), Now).Action);
        Assert.Equal(RuleAction.Allow, AppPolicyEvaluator.Evaluate(p, new(@"C:\Windows\System32\notepad.exe", null), Now).Action);
    }

    [Fact]
    public void Program_files_trusted_only_when_enabled_and_never_over_block()
    {
        var p = new AppPolicy { Rules = { new AppRule { Action = RuleAction.Block, FileName = "steam.exe" } } };
        var trusted = AppPolicyEvaluator.TrustedPrefixes(p, [@"C:\Program Files", @"C:\Program Files (x86)\"], @"C:\Program Files\GuardianHost");
        Assert.Equal(RuleAction.Allow, AppPolicyEvaluator.Evaluate(p, new(@"C:\Program Files (x86)\App\app.exe", null), Now, trusted).Action);
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(p, new(@"C:\Program Files (x86)\Steam\steam.exe", null), Now, trusted).Action);
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(p, new(@"C:\Users\kid\Downloads\x.exe", null), Now, trusted).Action);
        // Prefix must match a whole folder name.
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(p, new(@"C:\Program Files Evil\x.exe", null), Now, trusted).Action);

        p.TrustProgramFiles = false;
        trusted = AppPolicyEvaluator.TrustedPrefixes(p, [@"C:\Program Files"], @"C:\G");
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(p, new(@"C:\Program Files\App\app.exe", null), Now, trusted).Action);
        Assert.Equal(RuleAction.Allow, AppPolicyEvaluator.Evaluate(p, new(@"C:\G\g.exe", null), Now, trusted).Action);
    }

    [Fact]
    public void Hash_rule_survives_rename()
    {
        var p = new AppPolicy { Rules = { new AppRule { Action = RuleAction.Allow, Sha256 = "ABCD" } } };
        Assert.Equal(RuleAction.Allow, AppPolicyEvaluator.Evaluate(p, new(@"D:\x\renamed.exe", "abcd"), Now).Action);
    }

    [Fact]
    public void Block_wins_over_allow_only_during_schedule()
    {
        var p = new AppPolicy
        {
            Rules =
            {
                new AppRule { Action = RuleAction.Allow, PathPrefix = @"C:\Program Files\Game\" },
                new AppRule
                {
                    Action = RuleAction.Block, FileName = "game.exe",
                    When = new WeeklySchedule { Windows = { new([DayOfWeek.Monday], new(8, 0), new(14, 0)) } },
                },
            },
        };
        var proc = new ProcessInfo(@"C:\Program Files\Game\game.exe", null);
        Assert.Equal(RuleAction.Block, AppPolicyEvaluator.Evaluate(p, proc, Now).Action);
        Assert.Equal(RuleAction.Allow, AppPolicyEvaluator.Evaluate(p, proc, Now.AddHours(5)).Action);
    }
}

public class UrlPolicyTests
{
    [Theory]
    [InlineData("https://www.Example.com/path?q=1", "example.com", true)]
    [InlineData("example.com.", "*.example.com", true)]
    [InlineData("notexample.com", "example.com", false)]
    [InlineData("example.com:8080", "example.com", true)]
    public void Host_matching(string host, string domain, bool expected)
        => Assert.Equal(expected, UrlPolicy.HostMatches(host, domain));

    [Fact]
    public void Allowlist_and_blocklist_modes()
    {
        var p = new NetworkPolicy { Domains = { "bad.com" } };
        Assert.False(UrlPolicy.IsAllowed(p, "x.bad.com"));
        Assert.True(UrlPolicy.IsAllowed(p, "good.com"));
        p.Mode = UrlMode.Allowlist;
        Assert.True(UrlPolicy.IsAllowed(p, "bad.com"));
        Assert.False(UrlPolicy.IsAllowed(p, "good.com"));
    }
}

public class ServiceNameTests
{
    [Fact]
    public void Generated_names_are_valid_and_vary()
    {
        var names = Enumerable.Range(0, 20).Select(_ => ServiceNameGenerator.Generate()).ToList();
        Assert.All(names, n => Assert.True(ServiceNameGenerator.IsValid(n)));
        Assert.True(names.Distinct().Count() > 1);
        Assert.False(ServiceNameGenerator.IsValid("bad name"));
    }
}
