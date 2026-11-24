using System.Runtime.InteropServices;

namespace Guardian.Service.Native;

internal static class ProcessParent
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr h, int infoClass, ref PROCESS_BASIC_INFORMATION info, int size, out int returned);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr h, out long creation, out long exit, out long kernel, out long user);

    /// <summary>The path of the process that started <paramref name="pid"/>, or null if it is gone (or the id was reused).</summary>
    public static string? PathOf(int pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == IntPtr.Zero) return null;
        int parent;
        long childStart;
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            if (NtQueryInformationProcess(h, 0, ref pbi, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) != 0) return null;
            parent = (int)pbi.InheritedFromUniqueProcessId;
            if (!GetProcessTimes(h, out childStart, out _, out _, out _)) return null;
        }
        finally { CloseHandle(h); }
        var ph = OpenProcess(0x1000, false, parent);
        if (ph == IntPtr.Zero) return null;
        try
        {
            // A parent id that was reused by a newer process is not the real parent.
            if (!GetProcessTimes(ph, out var parentStart, out _, out _, out _) || parentStart > childStart) return null;
        }
        finally { CloseHandle(ph); }
        return ProcessPath.Get(parent);
    }
}
