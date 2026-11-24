using System.Collections.Concurrent;

namespace Guardian.Core.Network;

/// <summary>Short-lived cache of upstream answers, so pages and Windows' connectivity check stay fast
/// and keep working through brief upstream hiccups.</summary>
public sealed class DnsCache(Func<DateTime>? now = null)
{
    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly ConcurrentDictionary<(string, ushort), (byte[] Answer, DateTime Expires)> _items = new();
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromSeconds(60);
    /// <summary>Stale answers may be served this long when every upstream fails.</summary>
    public TimeSpan StaleGrace { get; init; } = TimeSpan.FromMinutes(30);

    public void Put(DnsMessage.Question q, byte[] answer)
    {
        if (answer.Length < 12 || (answer[3] & 0x0F) is not (0 or 3)) return; // cache NOERROR / NXDOMAIN only
        _items[(q.Name, q.Type)] = (answer, _now() + Lifetime);
        if (_items.Count > 5000) foreach (var k in _items.Where(kv => kv.Value.Expires < _now()).Select(kv => kv.Key).ToList()) _items.TryRemove(k, out _);
    }

    /// <summary>Cached answer re-stamped with the query's transaction id.</summary>
    public byte[]? Get(ReadOnlySpan<byte> query, DnsMessage.Question q, bool allowStale = false)
    {
        if (!_items.TryGetValue((q.Name, q.Type), out var e)) return null;
        if (e.Expires < _now() && !(allowStale && e.Expires + StaleGrace > _now())) return null;
        var copy = (byte[])e.Answer.Clone();
        copy[0] = query[0];
        copy[1] = query[1];
        return copy;
    }
}
