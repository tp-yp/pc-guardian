using Guardian.Core.Storage;
using Guardian.Service.Enforcers;

namespace Guardian.Service;

/// <summary>
/// Main loop: reloads the policy when it changes and runs each enforcer every few seconds.
/// One failing enforcer never stops the others.
/// </summary>
public sealed class EnforcementWorker(ILogger<EnforcementWorker> log, EncryptedConfigStore store, LockoutEnforcer lockout, AppBlocker apps,
    NetworkEnforcer network, DnsFilterServer dns, ProxyHost proxy) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var cfg = store.Load();
        var stamp = store.LastWriteUtc;
        log.LogInformation("Policy host started as {Name}. Lockout rules: {L}, app rules: {A}, domains: {D}",
            cfg.ServiceName, cfg.UserLockouts.Count, cfg.Apps.Rules.Count, cfg.Network.Domains.Count);

        dns.Update(cfg);
        proxy.Update(cfg);
        Run("network", () => network.Tick(cfg, force: true));
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            if (!store.Exists) break; // uninstalled: stop enforcing, the host is shutting down
            if (store.LastWriteUtc != stamp)
            {
                stamp = store.LastWriteUtc;
                cfg = store.Load();
                dns.Update(cfg);
                proxy.Update(cfg);
                Run("network", () => network.Tick(cfg, force: true));
                log.LogInformation("Policy reloaded.");
            }
            Run("lockout", () => lockout.Tick(cfg));
            Run("apps", () => apps.Tick(cfg));
            Run("network", () => network.Tick(cfg));
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
