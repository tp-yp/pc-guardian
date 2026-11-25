using System.Runtime.InteropServices;
using System.Security.Principal;
using Guardian.Core.Config;
using Guardian.Core.Policy;
using Microsoft.Win32;

namespace Guardian.Service.Enforcers;

/// <summary>
/// The Windows-native, instant layer of "block admin tools", per account: the command prompt is refused
/// (DisableCMD = 2, batch files still run), the registry editor is refused (DisableRegistryTools), and
/// File Explorer / Start / Run refuse to start the tools (DisallowRun). Closing the process (AppBlocker)
/// stays as the backstop for anything these policies do not cover, such as PowerShell inside a terminal.
/// Only values Guardian wrote are removed when the switch goes off, a temporary release starts, or Guardian is removed.
/// </summary>
public sealed class AdminToolsPolicy(ILogger<AdminToolsPolicy> log, AdminToolsExemptions exempt, TrustedClock clock)
{
    private const string Prefix = "Guardian-";
    private const string SystemPolicy = @"Software\Policies\Microsoft\Windows\System";
    private const string CvSystem = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string CvExplorer = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
    private static string StatePath => Path.Combine(Paths.DataDir, "tools-policy.txt");
    private DateTime _next = DateTime.MinValue;

    public void Tick(GuardianConfig cfg)
    {
        if (DateTime.UtcNow < _next) return;
        _next = DateTime.UtcNow.AddSeconds(15);
        var applied = ReadState();
        foreach (var sid in LoadedUserSids())
        {
            string? name = null;
            try { name = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value; } catch { }
            if (name is null) continue;
            var want = UserScopes.Effective(cfg, name).Apps.BlockAdminTools && !exempt.IsExempt(name, clock.LocalNow);
            if (want)
            {
                if (Apply(Registry.Users, sid) && applied.Add(sid)) log.LogInformation("Admin tools policy applied for {User}", name);
            }
            else if (applied.Remove(sid))
            {
                Remove(Registry.Users, sid);
                log.LogInformation("Admin tools policy removed for {User}", name);
            }
        }
        WriteState(applied);
    }

    private static IEnumerable<string> LoadedUserSids() =>
        Registry.Users.GetSubKeyNames().Where(n => n.StartsWith("S-1-5-21-") && !n.EndsWith("_Classes"));

    private static bool Apply(RegistryKey root, string sid)
    {
        using (var k = root.CreateSubKey($@"{sid}\{SystemPolicy}")) k.SetValue("DisableCMD", 2, RegistryValueKind.DWord);
        using (var k = root.CreateSubKey($@"{sid}\{CvSystem}")) k.SetValue("DisableRegistryTools", 1, RegistryValueKind.DWord);
        using (var k = root.CreateSubKey($@"{sid}\{CvExplorer}")) k.SetValue("DisallowRun", 1, RegistryValueKind.DWord);
        using (var k = root.CreateSubKey($@"{sid}\{CvExplorer}\DisallowRun"))
            foreach (var n in AdminTools.Names) k.SetValue(Prefix + n, n);
        return true;
    }

    private static void Remove(RegistryKey root, string sid)
    {
        Try(() => { using var k = root.OpenSubKey($@"{sid}\{SystemPolicy}", true); k?.DeleteValue("DisableCMD", false); });
        Try(() => { using var k = root.OpenSubKey($@"{sid}\{CvSystem}", true); k?.DeleteValue("DisableRegistryTools", false); });
        Try(() =>
        {
            using var list = root.OpenSubKey($@"{sid}\{CvExplorer}\DisallowRun", true);
            if (list != null) foreach (var v in list.GetValueNames().Where(v => v.StartsWith(Prefix))) list.DeleteValue(v, false);
            var empty = list != null && list.GetValueNames().Length == 0;
            list?.Dispose();
            using var ex = root.OpenSubKey($@"{sid}\{CvExplorer}", true);
            if (empty) { ex?.DeleteSubKey("DisallowRun", false); ex?.DeleteValue("DisallowRun", false); }
        });
    }

    /// <summary>On uninstall: remove the policies from every account Guardian touched, also when it is signed out.</summary>
    public static void RemoveAll()
    {
        var loaded = LoadedUserSids().ToHashSet();
        foreach (var sid in ReadState())
        {
            if (loaded.Contains(sid)) { Remove(Registry.Users, sid); continue; }
            Try(() => RemoveOffline(sid));
        }
        Try(() => File.Delete(StatePath));
    }

    private static void RemoveOffline(string sid)
    {
        using var prof = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
        var dir = prof?.GetValue("ProfileImagePath") as string;
        if (string.IsNullOrEmpty(dir)) return;
        var hive = Path.Combine(Environment.ExpandEnvironmentVariables(dir), "NTUSER.DAT");
        if (!File.Exists(hive)) return;
        EnablePrivilege("SeRestorePrivilege");
        EnablePrivilege("SeBackupPrivilege");
        const string mount = "GuardianOfflineUser";
        if (RegLoadKey(HKEY_USERS, mount, hive) != 0) return;
        try { Remove(Registry.Users, mount); }
        finally { GC.Collect(); GC.WaitForPendingFinalizers(); RegUnLoadKey(HKEY_USERS, mount); }
    }

    private static HashSet<string> ReadState()
    {
        try { return File.Exists(StatePath) ? File.ReadAllLines(StatePath).Where(l => l.StartsWith("S-1-5-21-")).ToHashSet() : []; }
        catch { return []; }
    }

    private static void WriteState(HashSet<string> sids)
    {
        try { File.WriteAllLines(StatePath, sids); } catch { }
    }

    private static void Try(Action a) { try { a(); } catch { } }

    private static readonly IntPtr HKEY_USERS = unchecked((IntPtr)(int)0x80000003);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegLoadKey(IntPtr hKey, string subKey, string file);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int RegUnLoadKey(IntPtr hKey, string subKey);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES { public int Count; public long Luid; public int Attributes; }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int len, IntPtr prev, IntPtr retLen);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x20 | 0x8, out var token)) return; // ADJUST_PRIVILEGES | QUERY
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) return;
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 }; // SE_PRIVILEGE_ENABLED
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }
}
