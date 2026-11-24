using System.Buffers.Binary;
using Guardian.Core.Config;
using Guardian.Core.Network;

namespace Guardian.Core.Tests;

public class DnsTests
{
    [Fact]
    public void Parses_question()
    {
        var q = DnsMessage.ReadQuestion(DnsMessage.BuildQuery(7, "WWW.Example.com", DnsMessage.TypeA))!;
        Assert.Equal("www.example.com", q.Name);
        Assert.Equal(DnsMessage.TypeA, q.Type);
    }

    [Fact]
    public void Rejects_garbage() => Assert.Null(DnsMessage.ReadQuestion(new byte[] { 1, 2, 3 }));

    [Fact]
    public void Blocked_A_answer_is_zero_address_with_same_id()
    {
        var resp = DnsMessage.BuildBlocked(DnsMessage.BuildQuery(0xBEEF, "bad.com", DnsMessage.TypeA));
        Assert.Equal(0xBEEF, BinaryPrimitives.ReadUInt16BigEndian(resp));
        Assert.True((resp[2] & 0x80) != 0);                         // response
        Assert.Equal(0, resp[3] & 0x0F);                            // NOERROR
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(resp.AsSpan(6))); // one answer
        Assert.Equal(new byte[4], resp[^4..]);
    }

    [Fact]
    public void Blocked_other_type_is_nxdomain()
    {
        var resp = DnsMessage.BuildBlocked(DnsMessage.BuildQuery(1, "bad.com", 16 /* TXT */));
        Assert.Equal(3, resp[3] & 0x0F);
    }

    [Fact]
    public void Filter_blocks_doh_and_keeps_windows_essentials_in_allowlist()
    {
        var p = new NetworkPolicy { Mode = UrlMode.Allowlist, Domains = { "wikipedia.org" } };
        Assert.True(DnsFilter.IsAllowed(p, "he.wikipedia.org"));
        Assert.True(DnsFilter.IsAllowed(p, "www.msftconnecttest.com"));
        Assert.False(DnsFilter.IsAllowed(p, "youtube.com"));
        p.Mode = UrlMode.Blocklist;
        Assert.False(DnsFilter.IsAllowed(p, "dns.google"));
        Assert.True(DnsFilter.IsAllowed(p, "youtube.com"));
    }

    [Fact]
    public void Cache_restamps_id_and_serves_stale_only_when_asked()
    {
        var now = new DateTime(2026, 1, 1);
        var cache = new DnsCache(() => now);
        var q1 = DnsMessage.BuildQuery(1, "a.com", DnsMessage.TypeA);
        var question = DnsMessage.ReadQuestion(q1)!;
        cache.Put(question, DnsMessage.BuildBlocked(q1)); // any NOERROR answer
        var q2 = DnsMessage.BuildQuery(0x1234, "a.com", DnsMessage.TypeA);
        var hit = cache.Get(q2, question)!;
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16BigEndian(hit));
        now = now.AddMinutes(5);
        Assert.Null(cache.Get(q2, question));
        Assert.NotNull(cache.Get(q2, question, allowStale: true));
    }

    [Fact]
    public void Servfail_answer()
    {
        var r = DnsMessage.BuildServFail(DnsMessage.BuildQuery(9, "x.com", DnsMessage.TypeA));
        Assert.Equal(2, r[3] & 0x0F);
        Assert.Equal(9, BinaryPrimitives.ReadUInt16BigEndian(r));
    }
}
