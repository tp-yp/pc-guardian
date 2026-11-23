using System.Text;
using Guardian.Core.Config;
using Guardian.Core.Network;
using Guardian.Core.Policy;

namespace Guardian.Core.Tests;

public class UrlPathPolicyTests
{
    [Theory]
    [InlineData("youtube.com/shorts", "www.youtube.com", "/shorts/abc", true)]
    [InlineData("youtube.com/shorts", "www.youtube.com", "/watch?v=1", false)]
    [InlineData("https://youtube.com/Shorts", "m.youtube.com", "/shorts", true)]
    [InlineData("youtube.com", "youtube.com", "", true)]
    public void Url_prefix_matching(string entry, string host, string path, bool expected)
        => Assert.Equal(expected, UrlPolicy.UrlMatches(entry, host, path));

    [Fact]
    public void Blocklist_path_entry_does_not_block_whole_host_at_dns_level()
    {
        var p = new NetworkPolicy { Domains = { "youtube.com/shorts", "bad.com" } };
        Assert.True(UrlPolicy.IsHostAllowed(p, "www.youtube.com"));
        Assert.False(UrlPolicy.IsHostAllowed(p, "bad.com"));
        Assert.False(UrlPolicy.IsUrlAllowed(p, "www.youtube.com", "/shorts/x"));
        Assert.True(UrlPolicy.IsUrlAllowed(p, "www.youtube.com", "/watch"));
    }

    [Fact]
    public void Allowlist_path_entry_opens_host_but_only_that_path()
    {
        var p = new NetworkPolicy { Mode = UrlMode.Allowlist, Domains = { "google.com/maps" } };
        Assert.True(UrlPolicy.IsHostAllowed(p, "www.google.com"));
        Assert.True(UrlPolicy.IsUrlAllowed(p, "www.google.com", "/maps/place/x"));
        Assert.False(UrlPolicy.IsUrlAllowed(p, "www.google.com", "/search?q=x"));
        Assert.False(UrlPolicy.IsHostAllowed(p, "bing.com"));
    }
}

public class HttpHeadTests
{
    private static HttpHead Parse(string s) => HttpHead.Parse(Encoding.ASCII.GetBytes(s))!;

    [Fact]
    public void Finds_end_of_head()
    {
        Assert.Equal(-1, HttpHead.FindEnd("GET / HTTP/1.1\r\nHost: a\r\n"u8));
        Assert.Equal(27, HttpHead.FindEnd("GET / HTTP/1.1\r\nHost: a\r\n\r\nBODY"u8));
    }

    [Fact]
    public void Connect_target()
    {
        var h = Parse("CONNECT www.Example.com:8443 HTTP/1.1\r\nHost: x\r\n\r\n");
        Assert.Equal(("www.example.com", 8443, ""), h.Resolve(tls: false));
    }

    [Fact]
    public void Absolute_form_proxy_request()
    {
        var h = Parse("GET http://a.com/x?y=1 HTTP/1.1\r\nHost: a.com\r\nProxy-Connection: keep-alive\r\n\r\n");
        Assert.Equal(("a.com", 80, "/x?y=1"), h.Resolve(tls: false));
        var up = Encoding.ASCII.GetString(h.ToUpstreamBytes("/x?y=1"));
        Assert.StartsWith("GET /x?y=1 HTTP/1.1\r\n", up);
        Assert.DoesNotContain("Proxy-Connection", up);
        Assert.Contains("Connection: close", up);
    }

    [Fact]
    public void Origin_form_inside_tls_uses_host_header()
    {
        var h = Parse("GET /watch HTTP/1.1\r\nHost: www.youtube.com\r\n\r\n");
        Assert.Equal(("www.youtube.com", 443, "/watch"), h.Resolve(tls: true));
    }

    [Fact]
    public void Websocket_upgrade_keeps_connection_header()
    {
        var h = Parse("GET /ws HTTP/1.1\r\nHost: a\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n\r\n");
        Assert.Contains("Connection: Upgrade", Encoding.ASCII.GetString(h.ToUpstreamBytes("/ws")));
    }

    [Fact]
    public void Rejects_non_http() => Assert.Null(HttpHead.Parse("\x16\x03\x01 garbage"u8));

    [Fact]
    public void Ipv6_host_port() => Assert.Equal(("::1", 8080), HttpHead.SplitHostPort("[::1]:8080", 80));

    [Fact]
    public void Blocked_page_escapes_url()
        => Assert.DoesNotContain("<script>", Encoding.UTF8.GetString(HttpHead.BlockedResponse("http://x/<script>")));
}
