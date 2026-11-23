using System.Collections.Concurrent;

namespace Guardian.Service.Enforcers;

public sealed record AccessEntry(DateTime Time, string Source, string Target, bool Allowed);

/// <summary>In-memory ring buffer of recent network decisions, for the management UI.</summary>
public sealed class AccessLog
{
    private const int Capacity = 2000;
    private readonly ConcurrentQueue<AccessEntry> _q = new();
    private string _lastKey = "";

    public void Add(string source, string target, bool allowed)
    {
        var key = source + target + allowed;
        if (key == _lastKey) return; // collapse bursts of identical lookups
        _lastKey = key;
        _q.Enqueue(new(DateTime.Now, source, target, allowed));
        while (_q.Count > Capacity) _q.TryDequeue(out _);
    }

    public IReadOnlyList<AccessEntry> Recent(int max = 200) => _q.Reverse().Take(max).ToList();
}
