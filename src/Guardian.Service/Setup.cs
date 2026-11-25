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

    /// <summary>
    /// Copies this exe into place, writes the config, and creates both services. The services are
    /// started before they are hardened: if Windows refuses to run them, everything is rolled back
    /// (the config is kept when <paramref name="keepConfigOnFailure"/>, so an update can be retried).
    /// </summary>
    private static void InstallWithConfig(EncryptedConfigStore store, GuardianConfig cfg, bool keepConfigOnFailure = false)
    {
        // Any service still pointing into the install folder is from an earlier attempt (the old version was
        // already uninstalled at this point), including ones with the same names that never started.
        try { ServiceHardening.RemoveLeftovers([]); Thread.Sleep(1000); } catch { /* best effort */ }
        // Folders may still be locked by an earlier attempt; the installer needs to write into them.
        DataDirSecurity.ReclaimForInstall(Paths.InstallDir);
        DataDirSecurity.ReclaimForInstall(Paths.DataDir);
        // An earlier removal may have scheduled these folders for deletion at the next restart; that
        // would wipe the fresh install (same config and key file names), so cancel it.
        Uninstaller.CancelScheduledDeletes();
        Directory.CreateDirectory(Paths.InstallDir);
        Directory.CreateDirectory(Paths.DataDir);
        var exe = Path.Combine(Paths.InstallDir, cfg.ServiceName + ".exe");
        CopyWithRetry(Environment.ProcessPath!, exe);
        store.Save(cfg);
        DataDirSecurity.Lock(Paths.DataDir);
        try
        {
            ServiceHardening.Create(cfg.ServiceName, exe);
            ServiceHardening.Create(cfg.WatchdogServiceName, exe, "watchdog");
            ServiceHardening.Start(cfg.ServiceName);
            ServiceHardening.Start(cfg.WatchdogServiceName);
        }
        catch
        {
            foreach (var n in new[] { cfg.ServiceName, cfg.WatchdogServiceName }) try { ServiceHardening.RemoveUnhardened(n); } catch { }
            Thread.Sleep(1000);
            try { File.Delete(exe); } catch { }
            if (!keepConfigOnFailure) try { Directory.Delete(Paths.DataDir, true); } catch { }
            throw;
        }
        ServiceHardening.Harden(cfg.ServiceName);
        ServiceHardening.Harden(cfg.WatchdogServiceName);
        Uninstaller.RegisterInWindows(exe);
        RemoveOldExes(exe);
        DataDirSecurity.Lock(Paths.InstallDir, usersCanRun: true);
    }

    /// <summary>Earlier installs used other random names; their copies stay behind in the install folder. Only the running copy is kept.</summary>
    private static void RemoveOldExes(string keep)
    {
        try
        {
            foreach (var f in Directory.GetFiles(Paths.InstallDir, "*.exe"))
                if (!string.Equals(f, keep, StringComparison.OrdinalIgnoreCase))
                    try { File.Delete(f); } catch { /* still in use: removed on a later install */ }
        }
        catch { /* best effort */ }
    }

    public enum SmartAppControl { Off, On, Evaluation, Unknown }

    /// <summary>Windows 11 Smart App Control. When it is on (or may switch itself on), Windows refuses to run Guardian's unsigned service.</summary>
    public static SmartAppControl SmartAppControlState()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy");
            return k?.GetValue("VerifiedAndReputablePolicyState") switch
            {
                0 => SmartAppControl.Off, 1 => SmartAppControl.On, 2 => SmartAppControl.Evaluation,
                null => SmartAppControl.Off, _ => SmartAppControl.Unknown,
            };
        }
        catch { return SmartAppControl.Unknown; }
    }

    /// <summary>A config left by an update whose install step failed: setup offers to finish it.</summary>
    public static bool HasPendingUpdate(EncryptedConfigStore store) => !IsInstalled() && store.Exists;

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
        if (IsInstalled())
        {
            var (ok, message) = Uninstall(store, password, forUpdate: true);
            if (!ok) return (false, message);
            // The old services and their processes need a moment to go away; installing too early
            // was why the first update attempt could fail and only a second run worked.
            WaitForOldVersionToStop([saved.ServiceName, saved.WatchdogServiceName]);
            // The uninstall removed the data folder and the settings file. Write the settings back right
            // away, so that if the install step fails, running the file again still updates (not a fresh setup).
            try { Uninstaller.CancelScheduledDeletes(); store.Save(saved); } catch { /* the install step saves again */ }
        }
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { InstallWithConfig(store, saved, keepConfigOnFailure: true); return (true, "Updated. All settings were kept."); }
            catch (BlockedByWindowsException ex) { return (false, ex.Message); }
            catch (Exception ex) { last = ex; Thread.Sleep(3000); }
        }
        try { if (!store.Exists) store.Save(saved); } catch { }
        return (false, "Install step failed: " + last!.Message + " Your settings are kept; run the file again to finish the update.");
    }

    /// <summary>Waits (up to 30 s) until the old services are deleted and nothing runs from the install folder.</summary>
    private static void WaitForOldVersionToStop(string[] services)
    {
        var until = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < until)
        {
            var left = System.ServiceProcess.ServiceController.GetServices().Any(s =>
                { using (s) return services.Contains(s.ServiceName, StringComparer.OrdinalIgnoreCase); });
            var running = System.Diagnostics.Process.GetProcesses().Any(p =>
            {
                using (p)
                {
                    if (p.Id == Environment.ProcessId) return false;
                    var path = Native.ProcessPath.Get(p.Id);
                    return path != null && path.StartsWith(Paths.InstallDir, StringComparison.OrdinalIgnoreCase);
                }
            });
            if (!left && !running) return;
            Thread.Sleep(500);
        }
    }

    private static void CopyWithRetry(string from, string to)
    {
        for (var i = 0; ; i++)
        {
            try { File.Copy(from, to, overwrite: true); return; }
            catch (Exception) when (i < 20) { Thread.Sleep(500); }
        }
    }

    private static string HashFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
    }

    /// <summary>Uninstalls with the admin or recovery password. Works even if the service is not running.</summary>
    public static (bool Ok, string Message) Uninstall(EncryptedConfigStore store, string password, bool forUpdate = false)
    {
        if (!store.Exists)
        {
            // An earlier removal stopped half-way: the settings (and with them every protection) are
            // already gone, so the rest is cleaned up without a password.
            if (!Uninstaller.HasLeftovers()) return (false, "Guardian is not installed.");
            Uninstaller.RemoveServicesAndFiles([]);
            return (true, "Guardian was removed. Leftovers of an earlier removal were cleaned up.");
        }
        string[] services;
        try
        {
            var resp = ControlPipeServer.Send(new ControlRequest(forUpdate ? "uninstall-update" : "uninstall", password), timeoutMs: 5000);
            // Versions before 0.9 do not know "uninstall-update" (and have no delay before turning off).
            if (forUpdate && !resp.Ok && resp.Message == "Unknown command.")
                resp = ControlPipeServer.Send(new ControlRequest("uninstall", password), timeoutMs: 5000);
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
            if (!forUpdate && cfg.Cooling.Hours > 0 && !(cfg.Cooling.UninstallDueUtc is { } due && due <= DateTime.UtcNow))
                return (false, "ההסרה מושהית (השהיית כיבוי). בקשו הסרה בחלון הניהול והמתינו לזמן שנקבע.");
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
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(int pid);

    public static void AttachParentConsole()
    {
        if (!AttachConsole(-1)) return; // ATTACH_PARENT_PROCESS
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch { /* no usable console */ }
    }

    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] private static extern uint GetConsoleProcessList(uint[] list, uint count);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    /// <summary>Hides the console window when it was created just for us (double-click), not when run from a terminal.</summary>
    public static void HideOwnConsole()
    {
        if (GetConsoleProcessList(new uint[2], 2) <= 1) ShowWindow(GetConsoleWindow(), 0);
    }
}
