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

    public static string Install(EncryptedConfigStore store, string adminPassword, string recoveryPassword, string? name = null)
    {
        name ??= ServiceNameGenerator.Generate();
        if (!ServiceNameGenerator.IsValid(name)) throw new ArgumentException("Invalid service name (letters/digits only).");
        if (adminPassword.Length < 8 || recoveryPassword.Length < 8) throw new ArgumentException("Passwords must be at least 8 characters.");
        if (adminPassword == recoveryPassword) throw new ArgumentException("The recovery password must differ from the admin password.");

        Directory.CreateDirectory(Paths.InstallDir);
        var exe = Path.Combine(Paths.InstallDir, name + ".exe");
        File.Copy(Environment.ProcessPath!, exe, overwrite: true);

        var watchdogName = ServiceNameGenerator.Generate();
        while (watchdogName == name) watchdogName = ServiceNameGenerator.Generate();
        store.Save(GuardianConfig.CreateInitial(name, watchdogName,
            PasswordHasher.Hash(adminPassword), PasswordHasher.Hash(recoveryPassword)));
        DataDirSecurity.Lock(Paths.DataDir);
        ServiceHardening.Install(name, exe);
        ServiceHardening.Install(watchdogName, exe, "watchdog");
        Uninstaller.RegisterInWindows(exe);
        DataDirSecurity.Lock(Paths.InstallDir);
        return name;
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
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
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
