using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;

namespace Guardian.Service.Enforcers;

/// <summary>
/// The DNS servers and DNS suffix the connected network hands out (by DHCP), even though Windows itself
/// is pointed at Guardian's filter. Used only for the network's internal names. Refreshed every 30 seconds,
/// so moving between home and school picks up the new network.
/// </summary>
public static class NetworkDns
{
    public sealed record Info(IReadOnlyList<IPAddress> Servers, IReadOnlyList<string> Suffixes);

    private static readonly object Gate = new();
    private static Info _info = new([], []);
    private static DateTime _at = DateTime.MinValue;

    public static Info Current()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _at < TimeSpan.FromSeconds(30)) return _info;
            var servers = new List<IPAddress>();
            var suffixes = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up &&
                             n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)))
                {
                    var props = nic.GetIPProperties();
                    if (!string.IsNullOrWhiteSpace(props.DnsSuffix)) suffixes.Add(props.DnsSuffix);
                    using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{nic.Id}");
                    if (k is null) continue;
                    foreach (var v in (k.GetValue("DhcpNameServer") as string ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                        if (IPAddress.TryParse(v, out var ip) && !IPAddress.IsLoopback(ip) && !servers.Contains(ip)) servers.Add(ip);
                    if (k.GetValue("DhcpDomain") is string d && d.Trim().Length > 0) suffixes.Add(d.Trim());
                }
            }
            catch (Exception) { /* keep what was found */ }
            _info = new Info(servers, suffixes.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            _at = DateTime.UtcNow;
            return _info;
        }
    }

    /// <summary>Asks the network's own DNS servers (plain DNS, 2 seconds each). Null if none answered.</summary>
    public static async Task<byte[]?> AskAsync(IReadOnlyList<IPAddress> servers, byte[] query, CancellationToken ct)
    {
        foreach (var server in servers.Take(3))
        {
            try
            {
                using var udp = new UdpClient(server.AddressFamily);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                await udp.SendAsync(query, new IPEndPoint(server, 53), cts.Token);
                var r = await udp.ReceiveAsync(cts.Token);
                if (r.Buffer.Length >= 12 && r.Buffer[0] == query[0] && r.Buffer[1] == query[1]) return r.Buffer;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException) { if (ct.IsCancellationRequested) throw; }
        }
        return null;
    }
}
