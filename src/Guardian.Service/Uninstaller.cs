using System.Diagnostics;
using Guardian.Core.Config;
using Guardian.Service.Enforcers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Guardian.Service;

/// <summary>
/// Everything that undoes an install. <see cref="LiftProtections"/> must run as SYSTEM
/// (inside the service, or the SYSTEM fallback task) because only SYSTEM may change the
/// hardened service permissions.
/// </summary>
public static class Uninstaller
{
    public const string ArpKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Guardian";
    public static string StartMenuLink =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Geder.lnk");
    /// <summary>Links made by older versions (before the program was called Geder).</summary>
    private static IEnumerable<string> OldStartMenuLinks => new[] { "Guardian.url", "Guardian.lnk" }
        .Select(n => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), n));

    /// <summary>Returns the service names to stop and delete afterwards.</summary>
    public static string[] LiftProtections(GuardianConfig cfg, NetworkEnforcer? network = null)
    {
        var services = new[] { cfg.ServiceName, cfg.WatchdogServiceName }.Where(n => n.Length > 0).ToArray();
        foreach (var svc in services)
        {
            Try(() => ServiceHardening.Unharden(svc));
            Try(() => ServiceHardening.DisableRecovery(svc));
        }
        Try(() => LockoutEnforcer.RestoreAll(cfg));
        Try(AccountGuard.RestoreAll); // new accounts Geder turned off are turned on again
        Try(() => (network ?? new NetworkEnforcer(NullLogger<NetworkEnforcer>.Instance)).Restore());
        Try(ProxyHost.RemoveCa);
        Try(BootGuard.Restore); // recovery environment, Safe Mode, F8 menu back as they were
        Try(() => WifiGuard.Restore(cfg));
        Try(AdminToolsPolicy.RemoveAll);
        Try(DnsOrigins.TurnLogOff);
        Try(BrowserPrivacy.Remove); // private browsing allowed again // the DNS client log, if Guardian turned it on // command prompt / registry editor / Run policies of each account // Wi-Fi filters off: any network can be joined again
        Try(() => Run("auditpol.exe", "/set /subcategory:\"{0CCE9226-69AE-11D9-BED3-505054503030}\" /failure:disable"));
        Try(() => DataDirSecurity.Unlock(Paths.InstallDir));
        Try(() => File.Delete(Path.Combine(Paths.DataDir, "config.bin")));
        Try(() => File.Delete(Path.Combine(Paths.DataDir, "key.bin")));
        Try(() => DataDirSecurity.Unlock(Paths.DataDir));
        return services;
    }

    /// <summary>
    /// Runs as the (admin) user after protections are lifted. Waits until the services have really
    /// stopped, closes any Guardian window still open, and retries deleting the files; whatever is
    /// still locked is deleted at the next restart, so nothing is left half-removed.
    /// </summary>
    public static void RemoveServicesAndFiles(IEnumerable<string> services)
    {
        foreach (var svc in services)
        {
            Try(() => Run("sc.exe", $"stop {svc}"));
            Try(() =>
            {
                using var sc = new System.ServiceProcess.ServiceController(svc);
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
            });
            Try(() => Run("sc.exe", $"delete {svc}"));
        }
        Try(() => ServiceHardening.RemoveLeftovers([]));
        Try(CloseOtherGuardianProcesses);
        Try(() => Registry.LocalMachine.DeleteSubKeyTree(ArpKey, false));
        Try(() => File.Delete(StartMenuLink));
        foreach (var old in OldStartMenuLinks) Try(() => File.Delete(old));
        foreach (var dir in new[] { Paths.InstallDir, Paths.DataDir }) DeleteOrScheduleDelete(dir);
    }

    /// <summary>True if something of an earlier install is still there (files, services or the "Installed apps" entry).</summary>
    public static bool HasLeftovers()
    {
        using var arp = Registry.LocalMachine.OpenSubKey(ArpKey);
        return arp != null || Directory.Exists(Paths.InstallDir) || File.Exists(StartMenuLink) || OldStartMenuLinks.Any(File.Exists);
    }

    private static void CloseOtherGuardianProcesses()
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id == Environment.ProcessId) continue;
                var path = Native.ProcessPath.Get(p.Id);
                if (path != null && path.StartsWith(Paths.InstallDir, StringComparison.OrdinalIgnoreCase))
                    Try(() => { p.Kill(entireProcessTree: true); p.WaitForExit(5000); });
            }
        }
    }

    private static void DeleteOrScheduleDelete(string dir)
    {
        for (var i = 0; i < 10 && Directory.Exists(dir); i++)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception) { Thread.Sleep(500); }
        }
        if (!Directory.Exists(dir)) return;
        // Still locked: remove the rest when Windows restarts.
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) MoveFileEx(f, null, 4);
        foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length)) MoveFileEx(d, null, 4);
        MoveFileEx(dir, null, 4); // MOVEFILE_DELAY_UNTIL_REBOOT
    }

    /// <summary>Removes pending "delete at restart" entries for Guardian's folders (left by an earlier removal).</summary>
    public static void CancelScheduledDeletes()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager", writable: true);
        if (k?.GetValue("PendingFileRenameOperations") is not string[] ops || ops.Length < 2) return;
        static bool Ours(string p)
        {
            var path = p.StartsWith(@"\??\") ? p[4..] : p.TrimStart('!');
            if (path.StartsWith(@"\??\")) path = path[4..];
            return new[] { Paths.InstallDir, Paths.DataDir }.Any(d =>
                path.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(d.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
        }
        var kept = new List<string>();
        for (var i = 0; i + 1 < ops.Length; i += 2)
            if (!(ops[i + 1].Length == 0 && Ours(ops[i]))) { kept.Add(ops[i]); kept.Add(ops[i + 1]); }
        if (kept.Count == ops.Length) return;
        if (kept.Count == 0) k.DeleteValue("PendingFileRenameOperations", false);
        else k.SetValue("PendingFileRenameOperations", kept.ToArray(), RegistryValueKind.MultiString);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string? replacement, int flags);

    /// <summary>Adds Guardian to "Installed apps" and a Start-menu link to the management page.</summary>
    public static void RegisterInWindows(string exe)
    {
        using (var k = Registry.LocalMachine.CreateSubKey(ArpKey))
        {
            k.SetValue("DisplayName", "Geder");
            k.SetValue("DisplayVersion", typeof(Uninstaller).Assembly.GetName().Version?.ToString(3) ?? "0.2.0");
            k.SetValue("Publisher", "Geder");
            k.SetValue("DisplayIcon", exe);
            k.SetValue("UninstallString", $"\"{exe}\" uninstall-gui");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        foreach (var old in OldStartMenuLinks) Try(() => File.Delete(old));
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic link = shell.CreateShortcut(StartMenuLink);
        link.TargetPath = exe;
        link.Arguments = "app";
        link.IconLocation = exe + ",0";
        link.Description = "Geder – ניהול";
        link.Save();
    }

    /// <summary>
    /// Fallback when the service does not answer (crashed, stuck, broken update): the caller has
    /// already verified the password against the config, then this runs a copy of the exe as SYSTEM
    /// through a one-shot scheduled task to lift the protections. Returns true on success.
    /// </summary>
    public static bool LiftProtectionsViaSystemTask(string password)
    {
        var dir = Path.Combine(Path.GetTempPath(), "GuardianUninstall-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "guardian-uninstall.exe");
        File.Copy(Environment.ProcessPath!, exe, true);
        var pwFile = Path.Combine(dir, "request");
        File.WriteAllText(pwFile, password);
        var done = Path.Combine(dir, "done");
        const string task = "GuardianUninstallOnce";
        try
        {
            Run("schtasks.exe", $"/Create /F /TN {task} /RU SYSTEM /RL HIGHEST /SC ONCE /ST 00:00 /TR \"\\\"{exe}\\\" uninstall-system \\\"{dir}\\\"\"");
            Run("schtasks.exe", $"/Run /TN {task}");
            for (var i = 0; i < 120 && !File.Exists(done); i++) Thread.Sleep(500);
            return File.Exists(done) && File.ReadAllText(done).StartsWith("ok");
        }
        finally
        {
            Run("schtasks.exe", $"/Delete /F /TN {task}");
            Try(() => File.Delete(pwFile));
        }
    }

    /// <summary>Entry point of the SYSTEM task. Re-checks the password itself.</summary>
    public static int SystemTaskMain(string dir, Core.Storage.EncryptedConfigStore store)
    {
        var done = Path.Combine(dir, "done");
        try
        {
            var pwFile = Path.Combine(dir, "request");
            var password = File.ReadAllText(pwFile);
            File.Delete(pwFile);
            var cfg = store.Load();
            var auth = new Core.Security.AdminGate().Authenticate(cfg, password);
            if (auth is not (Core.Security.AuthResult.Admin or Core.Security.AuthResult.Recovery))
            { File.WriteAllText(done, "denied"); return 2; }
            var services = LiftProtections(cfg);
            foreach (var svc in services) Try(() => ServiceHardening.StopAndDelete(svc));
            File.WriteAllText(done, "ok " + string.Join(",", services));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(done, "error " + ex.Message);
            return 1;
        }
    }

    private static void Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.WaitForExit(30_000);
    }

    private static void Try(Action a)
    {
        try { a(); } catch (Exception) { /* best effort: keep undoing the rest */ }
    }
}
