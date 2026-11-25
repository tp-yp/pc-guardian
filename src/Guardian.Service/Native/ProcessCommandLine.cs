using System.Runtime.InteropServices;

namespace Guardian.Service.Native;

/// <summary>The command line a process was started with (ProcessCommandLineInformation, Windows 8.1 and later).</summary>
internal static class ProcessCommandLine
{
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr h, int infoClass, IntPtr info, int size, out int returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    public static string? Get(int pid)
    {
        var h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var size = 4096;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var buf = Marshal.AllocHGlobal(size);
                try
                {
                    var st = NtQueryInformationProcess(h, 60, buf, size, out var needed);
                    if (st == unchecked((int)0xC0000004) && needed > size) { size = needed; continue; } // STATUS_INFO_LENGTH_MISMATCH
                    if (st != 0) return null;
                    // UNICODE_STRING { ushort Length; ushort MaximumLength; IntPtr Buffer; } followed by the text
                    var len = (ushort)Marshal.ReadInt16(buf);
                    var text = Marshal.ReadIntPtr(buf, IntPtr.Size);
                    return len == 0 || text == IntPtr.Zero ? "" : Marshal.PtrToStringUni(text, len / 2);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return null;
        }
        finally { CloseHandle(h); }
    }
}
