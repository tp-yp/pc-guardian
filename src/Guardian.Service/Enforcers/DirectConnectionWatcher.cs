using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text;

namespace Guardian.Service.Enforcers;

public sealed record DirectAttempt(string AppPath, string LastDestination, int Count, DateTime Last);

/// <summary>
/// Finds programs that do NOT go through the proxy: while full-URL filtering is on, Windows Firewall
/// blocks their direct connections, and with "Filtering Platform Connection" auditing enabled Windows
/// writes event 5157 for each one. We read those events, so the owner sees exactly which programs
/// need "direct internet" instead of guessing.
/// </summary>
public sealed class DirectConnectionWatcher(ILogger<DirectConnectionWatcher> log, AccessLog accessLog) : IDisposable
{
    private const string AuditSubcategory = "{0CCE9226-69AE-11D9-BED3-505054503030}"; // Filtering Platform Connection
    private const string Query = "*[System[EventID=5157]] and *[EventData[Data[@Name='Direction']='%%14593']]";
    private readonly ConcurrentDictionary<string, DirectAttempt> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _devices = DeviceMap();
    private EventLogWatcher? _watcher;

    public IReadOnlyList<DirectAttempt> Recent() => _seen.Values.OrderByDescending(a => a.Last).Take(100).ToList();

    public void SetActive(bool on)
    {
        if (on == (_watcher != null)) return;
        if (on)
        {
            Auditpol("/failure:enable");
            try
            {
                _watcher = new EventLogWatcher(new EventLogQuery("Security", PathType.LogName, Query));
                _watcher.EventRecordWritten += (_, e) => { using (e.EventRecord) Handle(e.EventRecord); };
                _watcher.Enabled = true;
                log.LogInformation("Watching for programs that bypass the proxy");
            }
            catch (Exception ex) { log.LogWarning(ex, "Cannot watch firewall events"); _watcher = null; }
        }
        else
        {
            _watcher?.Dispose();
            _watcher = null;
            Auditpol("/failure:disable");
        }
    }

    private void Handle(EventRecord? r)
    {
        if (r is null || r.Properties.Count < 8) return;
        var app = ToDosPath(r.Properties[1].Value?.ToString() ?? "");
        var dest = r.Properties[5].Value?.ToString() ?? "";
        var port = r.Properties[6].Value?.ToString() ?? "";
        if (app.Length == 0 || dest is "127.0.0.1" or "::1" || dest.StartsWith("224.") || dest.StartsWith("239.") || dest.StartsWith("ff0", StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(app, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return;
        var now = DateTime.Now;
        var prev = _seen.TryGetValue(app, out var p) ? p : null;
        _seen[app] = new DirectAttempt(app, $"{dest}:{port}", (prev?.Count ?? 0) + 1, now);
        if (prev is null || now - prev.Last > TimeSpan.FromMinutes(1)) accessLog.Add("direct", app, allowed: false, app: app);
    }

    /// <summary>\Device\HarddiskVolume3\Program Files\x.exe -> C:\Program Files\x.exe</summary>
    private string ToDosPath(string devicePath)
    {
        foreach (var (device, drive) in _devices)
            if (devicePath.StartsWith(device + "\\", StringComparison.OrdinalIgnoreCase))
                return drive + devicePath[device.Length..];
        return devicePath;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint QueryDosDevice(string device, StringBuilder target, int max);

    private static Dictionary<string, string> DeviceMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in DriveInfo.GetDrives())
        {
            var letter = d.Name.TrimEnd('\\');
            var sb = new StringBuilder(512);
            if (QueryDosDevice(letter, sb, sb.Capacity) > 0) map[sb.ToString()] = letter;
        }
        return map;
    }

    private void Auditpol(string flag)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("auditpol.exe", $"/set /subcategory:\"{AuditSubcategory}\" {flag}")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            p.WaitForExit(10_000);
        }
        catch (Exception ex) { log.LogWarning(ex, "auditpol failed"); }
    }

    public void Dispose() => SetActive(false);
}
