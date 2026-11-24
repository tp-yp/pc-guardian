using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Guardian.Service.Native;

/// <summary>Which Windows account a process runs as, and which process owns a local TCP connection.</summary>
internal static class ProcessUser
{
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_QUERY = 0x0008;
    private static readonly ConcurrentDictionary<int, (string? User, DateTime At)> Cache = new();

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

    /// <summary>"PC\name" for a process, or null (exited, protected, or a system account).</summary>
    public static string? Of(int pid)
    {
        if (pid <= 4) return null;
        if (Cache.TryGetValue(pid, out var c) && DateTime.UtcNow - c.At < TimeSpan.FromSeconds(30)) return c.User;
        string? user = null;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h != IntPtr.Zero)
        {
            try
            {
                if (OpenProcessToken(h, TOKEN_QUERY, out var tok))
                {
                    try
                    {
                        using var id = new WindowsIdentity(tok);
                        if (!(id.IsSystem || id.User is null || id.User.IsWellKnown(WellKnownSidType.LocalServiceSid) ||
                              id.User.IsWellKnown(WellKnownSidType.NetworkServiceSid)))
                            user = id.Name;
                    }
                    finally { CloseHandle(tok); }
                }
            }
            catch (Exception) { /* exited meanwhile */ }
            finally { CloseHandle(h); }
        }
        if (Cache.Count > 4096) Cache.Clear();
        Cache[pid] = (user, DateTime.UtcNow);
        return user;
    }

    // ---- TCP owner lookup (IPv4 and IPv6 tables) ----
    private const int AF_INET = 2, AF_INET6 = 23, TCP_TABLE_OWNER_PID_CONNECTIONS = 4;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

    /// <summary>
    /// PID of the local process whose connection uses <paramref name="clientPort"/> to reach our
    /// listener on <paramref name="serverPort"/> (both on loopback), or 0 if not found.
    /// </summary>
    public static int OwnerOfLoopbackConnection(int clientPort, int serverPort, bool v6)
    {
        var af = v6 ? AF_INET6 : AF_INET;
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buf = Marshal.AllocHGlobal(size);
            try
            {
                var rc = GetExtendedTcpTable(buf, ref size, false, af, TCP_TABLE_OWNER_PID_CONNECTIONS, 0);
                if (rc == 122) continue; // ERROR_INSUFFICIENT_BUFFER: table grew, retry with the new size
                if (rc != 0) return 0;
                var count = Marshal.ReadInt32(buf);
                // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid (6 x DWORD)
                // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScope, localPort, remoteAddr[16], remoteScope, remotePort, state, pid
                var rowSize = v6 ? 56 : 24;
                for (var i = 0; i < count; i++)
                {
                    var row = buf + 4 + i * rowSize;
                    int lport, rport, pid;
                    if (v6) { lport = Port(Marshal.ReadInt32(row, 20)); rport = Port(Marshal.ReadInt32(row, 44)); pid = Marshal.ReadInt32(row, 52); }
                    else { lport = Port(Marshal.ReadInt32(row, 8)); rport = Port(Marshal.ReadInt32(row, 16)); pid = Marshal.ReadInt32(row, 20); }
                    if (lport == clientPort && rport == serverPort) return pid;
                }
                return 0;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        return 0;
    }

    private static int Port(int raw) => (ushort)IPAddress.NetworkToHostOrder((short)(raw & 0xFFFF));
}
