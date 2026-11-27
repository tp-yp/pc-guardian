using System.Collections.Concurrent;

namespace Guardian.Service.Enforcers;

/// <param name="User">Windows account behind the entry, when known ("PC\name").</param>
/// <param name="App">The program behind the entry, when known (full path).</param>
public sealed record AccessEntry(DateTime Time, string Source, string Target, bool Allowed, string? User = null, string? App = null);

/// <summary>
/// Recent network and program decisions, for the management window, kept in memory for
/// <see cref="KeepHours"/> hours (up to a day; set in the settings), at most <see cref="Capacity"/> entries.
/// </summary>
public sealed class AccessLog
{
    private const int Capacity = 200_000;
    private readonly ConcurrentQueue<AccessEntry> _q = new();
    private string _lastKey = "";
    private int _adds;

    public int KeepHours { get; set; } = 24;

    /// <summary>Raised for every new entry (used to start the clock of sites with a time limit).</summary>
    public event Action<AccessEntry>? Added;

    public void Add(string source, string target, bool allowed, string? user = null, string? app = null)
    {
        var key = source + target + allowed + user + app;
        if (key == _lastKey) return; // collapse bursts of identical lookups
        _lastKey = key;
        var entry = new AccessEntry(DateTime.Now, source, target, allowed, string.IsNullOrEmpty(user) ? null : user, string.IsNullOrEmpty(app) ? null : app);
        _q.Enqueue(entry);
        try { Added?.Invoke(entry); } catch (Exception) { /* a listener never breaks logging */ }
        while (_q.Count > Capacity) _q.TryDequeue(out _);
        if (++_adds % 500 == 0) Trim();
    }

    private void Trim()
    {
        var cut = DateTime.Now.AddHours(-Math.Clamp(KeepHours, 1, 24));
        while (_q.TryPeek(out var e) && e.Time < cut) _q.TryDequeue(out _);
    }

    /// <summary>The newest entries first, from the last <paramref name="hours"/> hours.</summary>
    public IReadOnlyList<AccessEntry> Recent(int max = 200, double hours = 24)
    {
        Trim();
        var since = DateTime.Now.AddHours(-hours);
        return _q.Reverse().TakeWhile(e => e.Time >= since).Take(max).ToList();
    }
}
