using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using Guardian.Service.Native;

namespace Guardian.Service.Enforcers;

/// <summary>
/// Which program asked for a site name, for the activity list. Browsers with their own resolver are found
/// from the UDP port the question came from. Programs that ask through Windows' DNS service are found from
/// the DNS client's own event log (event 3006 is written by the asking program), which Guardian turns on
/// while site filtering is on and turns off again only if it was the one that turned it on.
/// </summary>
public sealed class DnsOrigins(ILogger<DnsOrigins> log) : IDisposable
{
    private const string Channel = "Microsoft-Windows-DNS-Client/Operational";
    private static string Marker => Path.Combine(Paths.DataDir, "dnslog-on.txt");
    private readonly ConcurrentDictionary<string, (int Pid, DateTime At)> _recent = new(StringComparer.OrdinalIgnoreCase);
    private EventLogWatcher? _watcher;

    public sealed record Origin(string? App, string? User);

    public void SetActive(bool on)
    {
        if (on == (_watcher != null)) return;
        if (on)
        {
            try
            {
                using var c = new EventLogConfiguration(Channel);
                if (!c.IsEnabled) { c.IsEnabled = true; c.SaveChanges(); File.WriteAllText(Marker, "1"); }
                _watcher = new EventLogWatcher(new EventLogQuery(Channel, PathType.LogName, "*[System[EventID=3006]]"));
                _watcher.EventRecordWritten += (_, e) => { using (e.EventRecord) Remember(e.EventRecord); };
                _watcher.Enabled = true;
            }
            catch (Exception ex) { log.LogDebug(ex, "DNS client log unavailable"); _watcher?.Dispose(); _watcher = null; }
        }
        else
        {
            _watcher?.Dispose();
            _watcher = null;
            TurnLogOff();
        }
    }

    /// <summary>Turns the DNS client log back off if Guardian turned it on (also used on removal).</summary>
    public static void TurnLogOff()
    {
        try
        {
            if (!File.Exists(Marker)) return;
            using var c = new EventLogConfiguration(Channel);
            if (c.IsEnabled) { c.IsEnabled = false; c.SaveChanges(); }
            File.Delete(Marker);
        }
        catch (Exception) { /* best effort */ }
    }

    private void Remember(EventRecord? r)
    {
        if (r?.ProcessId is not int pid || pid <= 4 || r.Properties.Count == 0) return;
        var name = r.Properties[0].Value?.ToString();
        if (string.IsNullOrEmpty(name)) return;
        _recent[name.TrimEnd('.')] = (pid, DateTime.UtcNow);
        if (_recent.Count > 4000)
            foreach (var k in _recent.Where(x => DateTime.UtcNow - x.Value.At > TimeSpan.FromSeconds(30)).Select(x => x.Key).ToList())
                _recent.TryRemove(k, out _);
    }

    /// <summary>The program and account behind a DNS question that arrived from <paramref name="port"/>.</summary>
    public Origin Find(string name, int port, bool v6)
    {
        var pid = ProcessUser.OwnerOfUdpPort(port, v6);
        var app = pid > 0 ? ProcessPath.Get(pid) : null;
        // Asked through Windows' DNS service: the program is the one that logged the question just before.
        if (app is null || Path.GetFileName(app).Equals("svchost.exe", StringComparison.OrdinalIgnoreCase))
        {
            if (_recent.TryGetValue(name.TrimEnd('.'), out var r) && DateTime.UtcNow - r.At < TimeSpan.FromSeconds(10))
            { pid = r.Pid; app = ProcessPath.Get(pid); }
            else return new(null, null);
        }
        return new(app, ProcessUser.Of(pid));
    }

    public void Dispose() => SetActive(false);
}
