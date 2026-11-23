using Guardian.Core.Storage;

namespace Guardian.Service;

/// <summary>
/// Main loop. Phase 1 only loads the config and heartbeats; the enforcers
/// (user lockout, app blocking, firewall/DNS) are added in later phases.
/// </summary>
public sealed class EnforcementWorker(ILogger<EnforcementWorker> log, EncryptedConfigStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var cfg = store.Load();
        log.LogInformation("Policy host started as {Name}. Lockout rules: {L}, app rules: {A}, domains: {D}",
            cfg.ServiceName, cfg.UserLockouts.Count, cfg.Apps.Rules.Count, cfg.Network.Domains.Count);

        while (!ct.IsCancellationRequested)
        {
            // TODO phase 2+: reload config on change, run enforcers.
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }
}
