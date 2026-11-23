using System.Text;

namespace Guardian.Core.Network;

/// <summary>An HTTP/1.x request head (request line + headers).</summary>
public sealed class HttpHead
{
    public string Method { get; private set; } = "";
    public string Target { get; private set; } = "";
    public string Version { get; private set; } = "";
    public List<KeyValuePair<string, string>> Headers { get; } = new();

    public const int MaxSize = 64 * 1024;

    /// <summary>Index just past "\r\n\r\n", or -1 if the head is incomplete.</summary>
    public static int FindEnd(ReadOnlySpan<byte> buf) => buf.IndexOf("\r\n\r\n"u8) is var i and >= 0 ? i + 4 : -1;

    public static HttpHead? Parse(ReadOnlySpan<byte> head)
    {
        var text = Encoding.Latin1.GetString(head);
        var lines = text.Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length != 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return null;
        var h = new HttpHead { Method = first[0], Target = first[1], Version = first[2] };
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) break;
            var colon = line.IndexOf(':');
            if (colon <= 0) return null;
            h.Headers.Add(new(line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }
        return h;
    }

    public string? Get(string name) =>
        Headers.FirstOrDefault(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    public void Set(string name, string value)
    {
        Headers.RemoveAll(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        Headers.Add(new(name, value));
    }

    public void Remove(string name) => Headers.RemoveAll(kv => kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Host, port and path of the request. CONNECT targets are "host:port".</summary>
    public (string Host, int Port, string Path) Resolve(bool tls)
    {
        if (Method == "CONNECT")
        {
            var (host, port) = SplitHostPort(Target, 443);
            return (host, port, "");
        }
        if (Uri.TryCreate(Target, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
            return (u.Host.ToLowerInvariant(), u.Port, u.PathAndQuery);
        var (h, p) = SplitHostPort(Get("Host") ?? "", tls ? 443 : 80);
        return (h, p, Target);
    }

    /// <summary>
    /// Prepares the head for the origin server: origin-form target, no proxy headers,
    /// and one request per connection (unless the client is upgrading, e.g. WebSocket).
    /// </summary>
    public byte[] ToUpstreamBytes(string path)
    {
        Remove("Proxy-Connection");
        Remove("Proxy-Authorization");
        var conn = Get("Connection") ?? "";
        if (!conn.Contains("upgrade", StringComparison.OrdinalIgnoreCase)) Set("Connection", "close");
        var sb = new StringBuilder();
        sb.Append(Method).Append(' ').Append(path.Length == 0 ? "/" : path).Append(' ').Append(Version).Append("\r\n");
        foreach (var (k, v) in Headers) sb.Append(k).Append(": ").Append(v).Append("\r\n");
        sb.Append("\r\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    public static (string Host, int Port) SplitHostPort(string s, int defaultPort)
    {
        if (s.StartsWith('['))
        {
            var end = s.IndexOf(']');
            var host = s[1..end];
            return (host, end + 2 < s.Length && int.TryParse(s[(end + 2)..], out var p6) ? p6 : defaultPort);
        }
        var colon = s.LastIndexOf(':');
        return colon > 0 && int.TryParse(s[(colon + 1)..], out var p)
            ? (s[..colon].ToLowerInvariant(), p)
            : (s.ToLowerInvariant(), defaultPort);
    }

    public static byte[] BlockedResponse(string url)
    {
        var body = $"<!doctype html><meta charset=utf-8><title>Blocked</title>" +
                   $"<body style=\"font-family:sans-serif;text-align:center;padding-top:15vh\">" +
                   $"<h1>&#128683; Blocked</h1><p>{System.Net.WebUtility.HtmlEncode(url)}</p></body>";
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = $"HTTP/1.1 403 Forbidden\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
        return [.. Encoding.ASCII.GetBytes(head), .. bytes];
    }
}
