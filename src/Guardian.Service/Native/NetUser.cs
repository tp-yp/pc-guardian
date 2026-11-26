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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct USER_INFO_1
    {
        public string name; public string password; public int password_age; public int priv;
        public string home_dir; public string comment; public int flags; public string script_path;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct USER_INFO_1008 { public int flags; }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserGetInfo(string? server, string user, int level, out IntPtr buf);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserSetInfo(string? server, string user, int level, ref USER_INFO_1008 buf, out int parmErr);
    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buf);

    private const int UF_ACCOUNTDISABLE = 0x2, USER_PRIV_ADMIN = 2;

    /// <summary>Flags and privilege level of a local account, or null if it is not a local account.</summary>
    private static (int Flags, int Priv)? Info(string userName)
    {
        if (NetUserGetInfo(null, StripDomain(userName), 1, out var buf) != 0) return null;
        try { var i = Marshal.PtrToStructure<USER_INFO_1>(buf); return (i.flags, i.priv); }
        finally { NetApiBufferFree(buf); }
    }

    public static bool IsAdmin(string userName) => Info(userName)?.Priv == USER_PRIV_ADMIN;
    public static bool IsDisabled(string userName) => (Info(userName)?.Flags & UF_ACCOUNTDISABLE) != 0;

    /// <summary>Turns a local account off or on (it cannot sign in or unlock while off). Returns NET_API_STATUS.</summary>
    public static int SetDisabled(string userName, bool disabled)
    {
        if (Info(userName) is not { } i) return 2221; // NERR_UserNotFound
        var flags = disabled ? i.Flags | UF_ACCOUNTDISABLE : i.Flags & ~UF_ACCOUNTDISABLE;
        if (flags == i.Flags) return 0;
        var info = new USER_INFO_1008 { flags = flags };
        return NetUserSetInfo(null, StripDomain(userName), 1008, ref info, out _);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct USER_INFO_20 { public string name; public string full_name; public string comment; public int flags; public int user_id; }
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserEnum(string? server, int level, int filter, out IntPtr buf, int prefMaxLen, out int read, out int total, ref int resume);

    /// <summary>Every ordinary local account, with its number, whether it is on and whether it is an administrator. Empty if listing fails.</summary>
    public static List<Guardian.Core.Lockout.LocalAccount> List()
    {
        var list = new List<Guardian.Core.Lockout.LocalAccount>();
        var resume = 0;
        int rc;
        do
        {
            rc = NetUserEnum(null, 20, 2 /* FILTER_NORMAL_ACCOUNT */, out var buf, -1, out var read, out _, ref resume);
            if (rc != 0 && rc != 234 /* ERROR_MORE_DATA */) return [];
            try
            {
                var size = Marshal.SizeOf<USER_INFO_20>();
                for (var i = 0; i < read; i++)
                {
                    var u = Marshal.PtrToStructure<USER_INFO_20>(buf + i * size);
                    list.Add(new(u.user_id, u.name, (u.flags & UF_ACCOUNTDISABLE) == 0, IsAdmin(u.name)));
                }
            }
            finally { if (buf != IntPtr.Zero) NetApiBufferFree(buf); }
        } while (rc == 234);
        return list;
    }

    public static byte[] AllHours => Enumerable.Repeat((byte)0xFF, 21).ToArray();

    private static string StripDomain(string u) => u.Contains('\\') ? u[(u.LastIndexOf('\\') + 1)..] : u;
}
