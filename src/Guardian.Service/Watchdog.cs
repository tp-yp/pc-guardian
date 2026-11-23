namespace Guardian.Service;

/// <summary>
/// Companion service ("watchdog" mode of the same exe). Each of the two services restarts the
/// other if it is found stopped; the SCM restarts either one if it crashes.
/// </summary>
public sealed class WatchdogWorker(ILogger<WatchdogWorker> log, Guardian.Core.Storage.EncryptedConfigStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            if (!store.Exists) break; // uninstalled
            try
            {
                var name = store.Load().ServiceName;
                if (ServiceHardening.EnsureRunning(name)) log.LogWarning("Main service {Name} was stopped; restarted", name);
            }
            catch (Exception ex) { log.LogDebug(ex, "watchdog check failed"); }
        }
    }
}
