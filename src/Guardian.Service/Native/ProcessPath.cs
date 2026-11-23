using System.Runtime.InteropServices;
using System.Text;

namespace Guardian.Service.Native;

internal static class ProcessPath
{
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    /// <summary>Full image path; works for 32/64-bit and elevated processes when running as SYSTEM.</summary>
    public static string? Get(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }
}
