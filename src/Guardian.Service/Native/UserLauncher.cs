using System.Runtime.InteropServices;

namespace Guardian.Service.Native;

/// <summary>Starts a process on a user's desktop from the SYSTEM service (for the "blocked" screen).</summary>
internal static class UserLauncher
{
    public static bool Launch(int sessionId, string exe, string args)
    {
        if (!WTSQueryUserToken(sessionId, out var userToken)) return false;
        IntPtr primary = IntPtr.Zero, env = IntPtr.Zero;
        try
        {
            if (!DuplicateTokenEx(userToken, 0x02000000 /* MAXIMUM_ALLOWED */, IntPtr.Zero, 2 /* Impersonation */, 1 /* Primary */, out primary)) return false;
            CreateEnvironmentBlock(out env, primary, false);
            var si = new STARTUPINFOW { cb = Marshal.SizeOf<STARTUPINFOW>(), lpDesktop = "winsta0\\default" };
            var cmd = $"\"{exe}\" {args}";
            if (!CreateProcessAsUserW(primary, null, cmd, IntPtr.Zero, IntPtr.Zero, false,
                    0x00000400 /* CREATE_UNICODE_ENVIRONMENT */ | 0x08000000 /* CREATE_NO_WINDOW */, env, null, ref si, out var pi))
                return false;
            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);
            return true;
        }
        finally
        {
            if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
            if (primary != IntPtr.Zero) CloseHandle(primary);
            CloseHandle(userToken);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOW
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSQueryUserToken(int sessionId, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(IntPtr tok, uint access, IntPtr attrs, int level, int type, out IntPtr newTok);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr tok, bool inherit);
    [DllImport("userenv.dll")] private static extern bool DestroyEnvironmentBlock(IntPtr env);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUserW(IntPtr tok, string? app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
        IntPtr env, string? dir, ref STARTUPINFOW si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
}
