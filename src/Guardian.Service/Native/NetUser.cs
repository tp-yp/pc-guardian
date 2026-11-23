using System.Runtime.InteropServices;

namespace Guardian.Service.Native;

internal static class NetUser
{
    [StructLayout(LayoutKind.Sequential)]
    private struct USER_INFO_1020 { public int usri1020_units_per_week; public IntPtr usri1020_logon_hours; }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserSetInfo(string? server, string user, int level, ref USER_INFO_1020 buf, out int parmErr);

    /// <summary>Sets the account's allowed logon hours (21-byte bitmap, UTC). Returns the NET_API_STATUS (0 = ok).</summary>
    public static int SetLogonHours(string userName, byte[] bits)
    {
        var mem = Marshal.AllocHGlobal(bits.Length);
        try
        {
            Marshal.Copy(bits, 0, mem, bits.Length);
            var info = new USER_INFO_1020 { usri1020_units_per_week = 168, usri1020_logon_hours = mem };
            return NetUserSetInfo(null, StripDomain(userName), 1020, ref info, out _);
        }
        finally { Marshal.FreeHGlobal(mem); }
    }

    public static byte[] AllHours => Enumerable.Repeat((byte)0xFF, 21).ToArray();

    private static string StripDomain(string u) => u.Contains('\\') ? u[(u.LastIndexOf('\\') + 1)..] : u;
}
