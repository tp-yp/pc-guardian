using System.Runtime.InteropServices;
using Guardian.Core.Lockout;

namespace Guardian.Service.Native;

internal static class Wts
{
    private static readonly IntPtr Server = IntPtr.Zero; // WTS_CURRENT_SERVER_HANDLE
    private const int WTSActive = 0, WTSDisconnected = 4;
    private const int WTSUserName = 5, WTSDomainName = 7;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WTS_SESSION_INFO { public int SessionId; public string pWinStationName; public int State; }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSEnumerateSessionsW(IntPtr h, int reserved, int version, out IntPtr info, out int count);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr p);
    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSQuerySessionInformationW(IntPtr h, int sessionId, int infoClass, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSLogoffSession(IntPtr h, int sessionId, bool wait);
    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WTSSendMessageW(IntPtr h, int sessionId, string title, int titleLen, string msg, int msgLen,
        int style, int timeout, out int response, bool wait);

    public static List<UserSession> UserSessions()
    {
        var list = new List<UserSession>();
        if (!WTSEnumerateSessionsW(Server, 0, 1, out var ptr, out var count)) return list;
        try
        {
            var size = Marshal.SizeOf<WTS_SESSION_INFO>();
            for (var i = 0; i < count; i++)
            {
                var s = Marshal.PtrToStructure<WTS_SESSION_INFO>(ptr + i * size);
                if (s.SessionId == 0 || (s.State != WTSActive && s.State != WTSDisconnected)) continue;
                var user = Query(s.SessionId, WTSUserName);
                if (string.IsNullOrEmpty(user)) continue;
                list.Add(new UserSession(s.SessionId, $"{Query(s.SessionId, WTSDomainName)}\\{user}"));
            }
        }
        finally { WTSFreeMemory(ptr); }
        return list;
    }

    public static bool Logoff(int sessionId) => WTSLogoffSession(Server, sessionId, false);

    public static void Notify(int sessionId, string title, string message) =>
        WTSSendMessageW(Server, sessionId, title, title.Length * 2, message, message.Length * 2,
            0x40 /* MB_ICONINFORMATION */, 60, out _, false);

    private static string Query(int sessionId, int cls)
    {
        if (!WTSQuerySessionInformationW(Server, sessionId, cls, out var buf, out _)) return "";
        try { return Marshal.PtrToStringUni(buf) ?? ""; } finally { WTSFreeMemory(buf); }
    }
}
