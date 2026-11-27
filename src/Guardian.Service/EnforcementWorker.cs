using Guardian.Core.Storage;
using Guardian.Service.Enforcers;

namespace Guardian.Service;

/// <summary>
/// Main loop: every second the policy is reloaded if it changed, programs are checked, locked screens are locked
/// again and new accounts are checked; the other enforcers run every five seconds.
/// One failing enforcer never stops the others.
/// </summary>
public sealed class EnforcementWorker(ILogger<EnforcementWorker> log, EncryptedConfigStore store, LockoutEnforcer lockout, AppBlocker apps,
    NetworkEnforcer network, DnsFilterServer dns, ProxyHost proxy,
    DirectConnectionWatcher direct, BootGuard boot, IspFilterMonitor isp, TrustedClock clock, WifiGuard wifi, AdminToolsPolicy toolsPolicy, DnsOrigins dnsOrigins, AccountGuard accounts, TimedSiteRunner timed) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var cfg = store.Load();
        // Settings from 0.9.6 or older: allowed folders and "Program Files counts as allowed" become scanned lists.
        try { if (FolderScan.MigrateOldRules(cfg)) { store.Save(cfg); log.LogInformation("Allowed folders turned into scanned program lists"); } }
        catch (Exception ex) { log.LogWarning(ex, "Allowed folders could not be scanned"); }
        var stamp = store.LastWriteUtc;
        log.LogInformation("Policy host started as {Name}. Lockout rules: {L}, app rules: {A}, domains: {D}",
            cfg.ServiceName, cfg.UserLockouts.Count, cfg.Apps.Rules.Count, cfg.Network.Domains.Count);

        // Older versions locked the program folder against normal users; fix that on every start.
        Run("acl", () => DataDirSecurity.Lock(Paths.InstallDir, usersCanRun: true));
        var first = timed.Tick(cfg) ?? cfg;
        dns.Update(first);
        proxy.Update(first);
        Run("network", () => network.Tick(cfg, force: true, isolated: isp.Isolated(cfg)));
        var slowAt = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            if (!store.Exists) break; // uninstalled: stop enforcing, the host is shutting down
            var reloaded = false;
            if (store.LastWriteUtc != stamp)
            {
                stamp = store.LastWriteUtc;
                cfg = store.Load();
                Run("network", () => network.Tick(cfg, force: true, isolated: isp.Isolated(cfg)));
                log.LogInformation("Policy reloaded.");
                reloaded = true;
            }
            // Sites with time rules: the filter gets new rules when one opens or closes (and after a reload).
            Run("timed sites", () => { if (timed.Tick(cfg) is { } eff) { dns.Update(eff); proxy.Update(eff); } });
            // Fast part: a new setting is in force within a second, and so is a re-lock or a new account.
            Run("apps", () => apps.Tick(cfg));
            Run("lockout", () => lockout.Tick(cfg));
            Run("accounts", () => accounts.Tick(cfg));
            Run("tools", () => toolsPolicy.Tick(cfg, reloaded));
            var ispChanged = false;
            Run("isp", () => ispChanged = isp.Tick(cfg));
            if (ispChanged) Run("network", () => network.Tick(cfg, force: true, isolated: isp.Isolated(cfg)));
            if (DateTime.UtcNow < slowAt && !reloaded) continue;
            slowAt = DateTime.UtcNow.AddSeconds(5);
            Run("clock", () => clock.Tick(cfg));
            Run("network", () => network.Tick(cfg, force: false, isolated: isp.Isolated(cfg)));
            Run("boot", () => boot.Tick(cfg));
            Run("wifi", () => wifi.Tick(cfg));
            Run("direct", () => direct.SetActive(cfg.Network.Enabled && cfg.Network.UrlFiltering));
            Run("dns origins", () => dnsOrigins.SetActive(cfg.Network.Enabled));
            Run("private browsing", () => BrowserPrivacy.Apply(cfg.Browsers.NoPrivate));
            Run("watchdog", () =>
            {
                if (cfg.WatchdogServiceName.Length > 0 && ServiceHardening.EnsureRunning(cfg.WatchdogServiceName))
                    log.LogWarning("Watchdog {Name} was stopped; restarted", cfg.WatchdogServiceName);
            });
        }
    }

    private void Run(string name, Action a)
    {
        try { a(); }
        catch (Exception ex) { log.LogError(ex, "Enforcer {Name} failed", name); }
    }
}
