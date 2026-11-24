using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Guardian.Core.Config;

namespace Guardian.Service.Enforcers;

/// <summary>
/// A clock that changing the Windows time does not move. The real time comes from the "Date" header of
/// well-known HTTPS sites (the TLS certificate is checked, so it cannot be faked on the way); two sites
/// must agree. Between checks, and when offline, time is counted with the computer's uptime counter,
/// which the Windows clock setting does not touch. The last good time is saved, so even after a restart
/// without internet the clock cannot be set back before it.
/// With the clock lock on, Windows' own clock and time zone are put back when someone changes them.
/// Limitation: switched off, offline and set forward before starting again cannot be told apart from real time passing.
/// </summary>
public sealed class TrustedClock
{
    private static readonly string[] Sources = ["https://www.google.com/", "https://www.microsoft.com/", "https://www.cloudflare.com/", "https://www.wikipedia.org/"];
    private static string StatePath => Path.Combine(Paths.DataDir, "clock.json");
    private readonly ILogger<TrustedClock> _log;
    private readonly HttpClient _http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
    private readonly object _gate = new();
    private DateTime _anchorUtc;
    private long _anchorTicks;
    private TimeSpan _nextSync = TimeSpan.Zero;
    private TimeSpan _nextSave = TimeSpan.Zero;
    private int _syncing;

    public TrustedClock(ILogger<TrustedClock> log)
    {
        _log = log;
        var saved = LoadSaved();
        var now = DateTime.UtcNow;
        // Until the first check: the Windows clock, but never earlier than the last time known to be right.
        _anchorUtc = saved is { } s && s > now ? s : now;
        _anchorTicks = Environment.TickCount64;
        if (saved is { } x && x > now) log.LogWarning("Windows clock is behind the last known time ({Saved:u}); using the saved time", x);
    }

    /// <summary>True once the time was confirmed from the internet since Guardian started.</summary>
    public bool Synced { get; private set; }
    public DateTime LastSync { get; private set; }

    public DateTime UtcNow
    {
        get { lock (_gate) return _anchorUtc + Since(_anchorTicks); }
    }

    /// <summary>Local time from the trusted clock, in the time zone that is kept (or the current one).</summary>
    public DateTime LocalNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, Zone);

    private volatile string _zoneId = "";
    private TimeZoneInfo Zone
    {
        get
        {
            if (_zoneId.Length > 0)
                try { return TimeZoneInfo.FindSystemTimeZoneById(_zoneId); } catch (Exception) { /* unknown id: use the current one */ }
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>Called by the main loop every few seconds.</summary>
    public void Tick(GuardianConfig cfg)
    {
        _zoneId = cfg.Clock.Locked ? cfg.Clock.TimeZoneId : "";
        if (Synced && Uptime() >= _nextSave) Save();
        if (Uptime() >= _nextSync) _ = SyncAsync();
        if (!cfg.Clock.Locked) return;
        KeepTimeZone(cfg.Clock.TimeZoneId);
        KeepWindowsClock();
    }

    // Counted with the uptime counter (keeps counting during sleep), which the Windows clock setting does not move.
    private static TimeSpan Uptime() => TimeSpan.FromMilliseconds(Environment.TickCount64);
    private static TimeSpan Since(long tick) => TimeSpan.FromMilliseconds(Environment.TickCount64 - tick);
    private TimeSpan _lastSyncUptime = TimeSpan.FromDays(-3650);
    private TimeSpan _lastFailUptime = TimeSpan.FromDays(-3650);

    public async Task SyncAsync()
    {
        if (Interlocked.Exchange(ref _syncing, 1) == 1) return;
        try
        {
            _nextSync = Uptime() + TimeSpan.FromMinutes(2); // retry soon if this fails
            var results = await Task.WhenAll(Sources.Select(Ask));
            // Bring every answer to "now" so they can be compared.
            var good = results.Where(r => r != null).Select(r => (Now: r!.Value.Utc + Since(r.Value.Stamp), r.Value.Utc, r.Value.Stamp))
                .OrderBy(r => r.Now).ToList();
            // At least two sources within a minute of each other.
            for (var i = good.Count - 1; i > 0; i--)
            {
                if (good[i].Now - good[i - 1].Now > TimeSpan.FromSeconds(60)) continue;
                lock (_gate) { _anchorUtc = good[i].Utc; _anchorTicks = good[i].Stamp; }
                Synced = true;
                LastSync = good[i].Utc;
                _lastSyncUptime = Uptime();
                _nextSync = Uptime() + TimeSpan.FromMinutes(30);
                Save();
                return;
            }
            _lastFailUptime = Uptime();
            _log.LogDebug("Time check: not enough sources agreed ({N})", good.Count);
        }
        catch (Exception ex) { _lastFailUptime = Uptime(); _log.LogDebug(ex, "Time check failed"); }
        finally { Interlocked.Exchange(ref _syncing, 0); }
    }

    /// <returns>The site's time at the moment the answer arrived (Stamp, an uptime-counter value).</returns>
    private async Task<(DateTime Utc, long Stamp)?> Ask(string url)
    {
        try
        {
            var sent = Environment.TickCount64;
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var res = await _http.SendAsync(req);
            var stamp = Environment.TickCount64;
            if (res.Headers.Date is not { } date) return null;
            // The header is rounded down to the second and was written about half-way through the round trip.
            return (date.UtcDateTime + TimeSpan.FromMilliseconds(500) + TimeSpan.FromMilliseconds(stamp - sent) / 2, stamp);
        }
        catch (Exception) { return null; }
    }

    private void KeepWindowsClock()
    {
        if (!Synced && LoadSaved() is null) return; // nothing better than the Windows clock to go by
        var trusted = UtcNow;
        var drift = DateTime.UtcNow - trusted;
        if (drift.Duration() < TimeSpan.FromMinutes(2)) return;
        // Offline after a restart, a clock that is ahead cannot be proven wrong; only "set back" is corrected then.
        if (!Synced && drift > TimeSpan.Zero) return;
        // Before moving the clock, confirm the difference with a fresh check (e.g. after waking from sleep).
        // Offline (the check just failed), the counted time is still the best there is.
        if (Synced && Uptime() - _lastSyncUptime > TimeSpan.FromMinutes(2) && Uptime() - _lastFailUptime > TimeSpan.FromMinutes(2)) { _ = SyncAsync(); return; }
        var st = new SYSTEMTIME(trusted);
        if (SetSystemTime(ref st)) _log.LogWarning("Windows clock was off by {Drift}; set back to {Time:u}", drift, trusted);
        else _log.LogWarning("Could not correct the Windows clock (error {Err})", Marshal.GetLastWin32Error());
    }

    private void KeepTimeZone(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        TimeZoneInfo.ClearCachedData();
        if (string.Equals(TimeZoneInfo.Local.Id, id, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var p = Process.Start(new ProcessStartInfo("tzutil.exe", $"/s \"{id}\"") { UseShellExecute = false, CreateNoWindow = true })!;
            p.WaitForExit(10_000);
            _log.LogWarning("Time zone was changed to {Other}; set back to {Id}", TimeZoneInfo.Local.Id, id);
            TimeZoneInfo.ClearCachedData();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Could not restore the time zone"); }
    }

    private void Save()
    {
        _nextSave = Uptime() + TimeSpan.FromMinutes(1);
        try { File.WriteAllText(StatePath, JsonSerializer.Serialize(new Saved(UtcNow))); }
        catch (Exception ex) { _log.LogDebug(ex, "Could not save the clock state"); }
    }

    private static DateTime? LoadSaved()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<Saved>(File.ReadAllText(StatePath))?.Utc : null; }
        catch (Exception) { return null; }
    }

    private sealed record Saved(DateTime Utc);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME(DateTime utc)
    {
        public ushort Year = (ushort)utc.Year, Month = (ushort)utc.Month, DayOfWeek = (ushort)utc.DayOfWeek, Day = (ushort)utc.Day,
            Hour = (ushort)utc.Hour, Minute = (ushort)utc.Minute, Second = (ushort)utc.Second, Milliseconds = (ushort)utc.Millisecond;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetSystemTime(ref SYSTEMTIME time);
}
