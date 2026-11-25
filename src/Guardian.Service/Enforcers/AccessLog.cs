using System.Collections.Concurrent;

namespace Guardian.Service.Enforcers;

/// <param name="User">Windows account behind the entry, when known ("PC\name").</param>
/// <param name="App">The program behind the entry, when known (full path).</param>
public sealed record AccessEntry(DateTime Time, string Source, string Target, bool Allowed, string? User = null, string? App = null);

/// <summary>In-memory ring buffer of recent network decisions, for the management UI.</summary>
public sealed class AccessLog
{
    private const int Capacity = 2000;
    private readonly ConcurrentQueue<AccessEntry> _q = new();
    private string _lastKey = "";

    public void Add(string source, string target, bool allowed, string? user = null, string? app = null)
    {
        var key = source + target + allowed + user + app;
        if (key == _lastKey) return; // collapse bursts of identical lookups
        _lastKey = key;
        _q.Enqueue(new(DateTime.Now, source, target, allowed, string.IsNullOrEmpty(user) ? null : user, string.IsNullOrEmpty(app) ? null : app));
        while (_q.Count > Capacity) _q.TryDequeue(out _);
    }

    public IReadOnlyList<AccessEntry> Recent(int max = 200) => _q.Reverse().Take(max).ToList();
}
