using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Guardian.Core.Config;
using Guardian.Core.Naming;
using Guardian.Core.Security;
using Guardian.Core.Storage;

namespace Guardian.Service;

/// <summary>Install / uninstall logic shared by the command line and the double-click GUI.</summary>
public static class Setup
{
    public static bool IsElevated() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Relaunches this exe elevated (UAC prompt) with the same arguments. Returns false if the user declined.</summary>
    public static bool RelaunchElevated(string[] args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, string.Join(" ", args.Select(a => $"\"{a}\"")))
                { UseShellExecute = true, Verb = "runas" });
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    public static string Install(EncryptedConfigStore store, string adminPassword, string recoveryPassword, string? name = null, int idleLockMinutes = 5)
    {
        name ??= ServiceNameGenerator.Generate();
        if (!ServiceNameGenerator.IsValid(name)) throw new ArgumentException("Invalid service name (letters/digits only).");
        if (adminPassword.Length < 8 || recoveryPassword.Length < 8) throw new ArgumentException("Passwords must be at least 8 characters.");
        if (adminPassword == recoveryPassword) throw new ArgumentException("The recovery password must differ from the admin password.");

        if (!GuardianConfig.IdleLockChoices.Contains(idleLockMinutes)) throw new ArgumentException("Choose when the settings lock themselves.");

        var watchdogName = ServiceNameGenerator.Generate();
        while (watchdogName == name) watchdogName = ServiceNameGenerator.Generate();
        var cfg = GuardianConfig.CreateInitial(name, watchdogName, PasswordHasher.Hash(adminPassword), PasswordHasher.Hash(recoveryPassword));
        cfg.UiIdleLockMinutes = idleLockMinutes;
        InstallWithConfig(store, cfg);
        return name;
    }

    /// <summary>Copies this exe into place, writes the config, and creates both protected services.</summary>
    private static void InstallWithConfig(EncryptedConfigStore store, GuardianConfig cfg)
    {
        Directory.CreateDirectory(Paths.InstallDir);
        var exe = Path.Combine(Paths.InstallDir, cfg.ServiceName + ".exe");
        File.Copy(Environment.ProcessPath!, exe, overwrite: true);
        store.Save(cfg);
        DataDirSecurity.Lock(Paths.DataDir);
        ServiceHardening.Install(cfg.ServiceName, exe);
        ServiceHardening.Install(cfg.WatchdogServiceName, exe, "watchdog");
        Uninstaller.RegisterInWindows(exe);
        DataDirSecurity.Lock(Paths.InstallDir, usersCanRun: true);
    }

    /// <summary>Installed? Read from "Installed apps" so it also works without admin rights.</summary>
    public static bool IsInstalled() => InstalledExe() != null;

    /// <summary>The installed exe, if any (from the "Installed apps" entry, readable by everyone).</summary>
    public static string? InstalledExe()
    {
        using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(Uninstaller.ArpKey);
        var p = k?.GetValue("DisplayIcon") as string;
        return p != null && File.Exists(p) ? p : null;
    }

    /// <summary>True when this exe is a different build from the installed one.</summary>
    public static bool IsDifferentBuildThanInstalled()
    {
        var installed = InstalledExe();
        if (installed is null) return false;
        if (string.Equals(Path.GetFullPath(installed), Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase)) return false;
        return HashFile(installed) != HashFile(Environment.ProcessPath!);
    }

    /// <summary>
    /// Update to this build, keeping every setting and both passwords: a clean uninstall
    /// (which works with any older version) followed by an install with the saved config.
    /// </summary>
    public static (bool Ok, string Message) Upgrade(EncryptedConfigStore store, string password)
    {
        if (!store.Exists) return (false, "Guardian is not installed.");
        var saved = store.Load();
        if (saved.UiIdleLockMinutes == 0) saved.UiIdleLockMinutes = 5;
        var auth = new AdminGate().Authenticate(saved, password);
        if (auth is not (AuthResult.Admin or AuthResult.Recovery)) return (false, "Wrong password.");
        var (ok, message) = Uninstall(store, password);
        if (!ok) return (false, message);
        Thread.Sleep(2000);
        try { InstallWithConfig(store, saved); }
        catch (Exception ex) { return (false, "Removed the old version but installing the new one failed: " + ex.Message + " Run the file again to install."); }
        return (true, "Updated. All settings were kept.");
    }

    private static string HashFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
    }

    /// <summary>Uninstalls with the admin or recovery password. Works even if the service is not running.</summary>
    public static (bool Ok, string Message) Uninstall(EncryptedConfigStore store, string password)
    {
        if (!store.Exists) return (false, "Guardian is not installed.");
        string[] services;
        try
        {
            var resp = ControlPipeServer.Send(new ControlRequest("uninstall", password), timeoutMs: 5000);
            if (!resp.Ok) return (false, resp.Message);
            services = resp.Message.Split(',', StringSplitOptions.RemoveEmptyEntries);
            Thread.Sleep(1500);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The service is not answering. Check the password here, then lift protections as SYSTEM.
            var cfg = store.Load();
            var auth = new AdminGate().Authenticate(cfg, password);
            if (auth is not (AuthResult.Admin or AuthResult.Recovery)) return (false, "Access denied.");
            services = new[] { cfg.ServiceName, cfg.WatchdogServiceName }.Where(n => n.Length > 0).ToArray();
            if (!Uninstaller.LiftProtectionsViaSystemTask(password))
                return (false, "The service did not answer and the fallback failed. Restart the computer and try again.");
        }
        Uninstaller.RemoveServicesAndFiles(services);
        return (true, "Guardian was removed. Everything it changed has been restored.");
    }

    public static void OpenUi() => OpenUrl("http://127.0.0.1:8898/");

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // ---- console handling for double-click launches ----
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] private static extern uint GetConsoleProcessList(uint[] list, uint count);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    /// <summary>Hides the console window when it was created just for us (double-click), not when run from a terminal.</summary>
    public static void HideOwnConsole()
    {
        if (GetConsoleProcessList(new uint[2], 2) <= 1) ShowWindow(GetConsoleWindow(), 0);
    }
}
