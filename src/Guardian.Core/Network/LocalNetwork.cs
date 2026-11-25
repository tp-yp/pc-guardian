using System.Net;
using System.Net.Sockets;

namespace Guardian.Core.Network;

/// <summary>
/// Addresses on the home network (a printer, a NAS, a local server) that are left out of the filter.
/// Entries are one address ("192.168.1.20") or a range in CIDR form ("192.168.1.0/24"), and must lie
/// inside the private ranges only, so an entry can never open the internet itself.
/// </summary>
public static class LocalNetwork
{
    public readonly record struct Range(bool V6, UInt128 Lo, UInt128 Hi);

    /// <summary>Private ranges: 10/8, 172.16/12, 192.168/16, 169.254/16 (link-local), fc00::/7, fe80::/10.</summary>
    private static readonly Range[] Private =
    [
        Cidr(IPAddress.Parse("10.0.0.0"), 8), Cidr(IPAddress.Parse("172.16.0.0"), 12),
        Cidr(IPAddress.Parse("192.168.0.0"), 16), Cidr(IPAddress.Parse("169.254.0.0"), 16),
        Cidr(IPAddress.Parse("fc00::"), 7), Cidr(IPAddress.Parse("fe80::"), 10),
    ];

    public const string Error = "אפשר להחריג רק כתובת ברשת המקומית, למשל 192.168.1.20 או 192.168.1.0/24.";

    /// <summary>Checks an entry and returns it in a standard form.</summary>
    public static bool TryNormalize(string input, out string normalized, out string error)
    {
        normalized = ""; error = Error;
        if (!TryParse(input, out var r, out var text)) return false;
        if (!Private.Any(p => p.V6 == r.V6 && p.Lo <= r.Lo && r.Hi <= p.Hi)) return false;
        normalized = text; error = "";
        return true;
    }

    /// <summary>True when <paramref name="host"/> is an address (not a name) inside one of the entries.</summary>
    public static bool Contains(IEnumerable<string> entries, string host)
    {
        var h = host.Trim().TrimStart('[').TrimEnd(']');
        var pct = h.IndexOf('%');
        if (pct > 0) h = h[..pct];
        if (!IPAddress.TryParse(h, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var (v6, n) = ToNumber(ip);
        foreach (var e in entries)
            if (TryNormalize(e, out var norm, out _) && TryParse(norm, out var r, out _) && r.V6 == v6 && r.Lo <= n && n <= r.Hi)
                return true;
        return false;
    }

    /// <summary>The entries as Windows Firewall address ranges ("a-b"), or "" if there are none.</summary>
    public static string FirewallList(IEnumerable<string> entries) => Format(Valid(entries));

    /// <summary>
    /// A Windows Firewall address list ("a-b,c-d") with the entries taken out, so a block rule
    /// written with it leaves the home-network addresses alone.
    /// </summary>
    public static string Subtract(string firewallList, IEnumerable<string> entries)
    {
        var holes = Valid(entries);
        if (holes.Count == 0) return firewallList;
        var result = new List<Range>();
        foreach (var part in firewallList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-');
            var lo = ToNumber(IPAddress.Parse(dash < 0 ? part : part[..dash]));
            var hi = ToNumber(IPAddress.Parse(dash < 0 ? part : part[(dash + 1)..]));
            var pieces = new List<Range> { new(lo.V6, lo.N, hi.N) };
            foreach (var h in holes.Where(h => h.V6 == lo.V6))
            {
                var next = new List<Range>();
                foreach (var p in pieces)
                {
                    if (h.Hi < p.Lo || h.Lo > p.Hi) { next.Add(p); continue; }
                    if (h.Lo > p.Lo) next.Add(p with { Hi = h.Lo - 1 });
                    if (h.Hi < p.Hi) next.Add(p with { Lo = h.Hi + 1 });
                }
                pieces = next;
            }
            result.AddRange(pieces);
        }
        return Format(result);
    }

    private static List<Range> Valid(IEnumerable<string> entries)
    {
        var list = new List<Range>();
        foreach (var e in entries)
            if (TryNormalize(e, out var norm, out _) && TryParse(norm, out var r, out _)) list.Add(r);
        return list;
    }

    private static string Format(IEnumerable<Range> ranges) => string.Join(",", ranges.Select(r =>
        r.Lo == r.Hi ? ToAddress(r.V6, r.Lo).ToString() : ToAddress(r.V6, r.Lo) + "-" + ToAddress(r.V6, r.Hi)));

    private static bool TryParse(string input, out Range range, out string text)
    {
        range = default; text = "";
        var s = (input ?? "").Trim();
        if (s.Length is 0 or > 60) return false;
        var slash = s.IndexOf('/');
        var addr = slash < 0 ? s : s[..slash];
        if (!IPAddress.TryParse(addr, out var ip) || addr.Contains('%')) return false;
        var max = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var bits = max;
        if (slash >= 0 && (!int.TryParse(s[(slash + 1)..], out bits) || bits < 0 || bits > max)) return false;
        range = Cidr(ip, bits);
        var start = ToAddress(range.V6, range.Lo);
        text = bits == max ? start.ToString() : start + "/" + bits;
        return true;
    }

    private static Range Cidr(IPAddress ip, int bits)
    {
        var (v6, n) = ToNumber(ip);
        var max = v6 ? 128 : 32;
        var hostBits = max - bits;
        var hostMask = hostBits == 0 ? UInt128.Zero : hostBits == 128 ? UInt128.MaxValue : (UInt128.One << hostBits) - 1;
        var lo = n & ~hostMask;
        return new Range(v6, lo, lo | hostMask);
    }

    private static (bool V6, UInt128 N) ToNumber(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        UInt128 n = 0;
        foreach (var x in b) n = (n << 8) | x;
        return (b.Length == 16, n);
    }

    private static IPAddress ToAddress(bool v6, UInt128 n)
    {
        var b = new byte[v6 ? 16 : 4];
        for (var i = b.Length - 1; i >= 0; i--) { b[i] = (byte)(n & 0xFF); n >>= 8; }
        return new IPAddress(b);
    }
}
