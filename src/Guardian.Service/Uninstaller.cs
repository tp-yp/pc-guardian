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
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Guardian.url");

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
        Try(() => (network ?? new NetworkEnforcer(NullLogger<NetworkEnforcer>.Instance)).Restore());
        Try(ProxyHost.RemoveCa);
        Try(() => DataDirSecurity.Unlock(Paths.InstallDir));
        Try(() => File.Delete(Path.Combine(Paths.DataDir, "config.bin")));
        Try(() => File.Delete(Path.Combine(Paths.DataDir, "key.bin")));
        Try(() => DataDirSecurity.Unlock(Paths.DataDir));
        return services;
    }

    /// <summary>Runs as the (admin) user after protections are lifted.</summary>
    public static void RemoveServicesAndFiles(IEnumerable<string> services)
    {
        foreach (var svc in services) Try(() => ServiceHardening.StopAndDelete(svc));
        Thread.Sleep(1500);
        Try(() => Registry.LocalMachine.DeleteSubKeyTree(ArpKey, false));
        Try(() => File.Delete(StartMenuLink));
        Try(() => Directory.Delete(Paths.InstallDir, recursive: true));
        Try(() => Directory.Delete(Paths.DataDir, recursive: true));
    }

    /// <summary>Adds Guardian to "Installed apps" and a Start-menu link to the management page.</summary>
    public static void RegisterInWindows(string exe)
    {
        using (var k = Registry.LocalMachine.CreateSubKey(ArpKey))
        {
            k.SetValue("DisplayName", "Guardian");
            k.SetValue("DisplayVersion", typeof(Uninstaller).Assembly.GetName().Version?.ToString(3) ?? "0.2.0");
            k.SetValue("Publisher", "Guardian");
            k.SetValue("DisplayIcon", exe);
            k.SetValue("UninstallString", $"\"{exe}\" uninstall-gui");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        File.WriteAllText(StartMenuLink, "[InternetShortcut]\r\nURL=http://127.0.0.1:8898/\r\n");
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
