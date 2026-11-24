using System.Diagnostics;

namespace Guardian.Service;

/// <summary>
/// Legitimate anti-tamper for a self-control tool (the same approach as commercial blockers):
///  * service starts automatically and is restarted by the SCM if it dies;
///  * the service DACL removes STOP / DELETE / CHANGE_CONFIG / WRITE_DAC from Administrators,
///    so "sc stop", services.msc and "sc delete" fail unless the password-protected uninstall
///    (which runs as SYSTEM through this tool) restores the default DACL first.
/// This raises the bar; it does NOT make the service unkillable by a determined administrator
/// (take-ownership, Safe Mode, offline registry edit, reinstalling Windows all still work).
/// </summary>
public static class ServiceHardening
{
    // SY = full control. BA/IU/SU = query/interrogate/start only (no WP=stop, SD=delete, DC=change config, WD=write DAC).
    public const string HardenedSddl =
        "D:(A;;CCLCSWRPWPDTLOCRSDRCWDWO;;;SY)(A;;CCLCSWRPLOCRRC;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

    // Windows default service DACL.
    public const string DefaultSddl =
        "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

    /// <summary>Creates the service (not yet hardened, so it can still be removed if it fails to start).</summary>
    public static void Create(string name, string exePath, string mode = "run")
    {
        Sc($"create {name} binPath= \"\\\"{exePath}\\\" {mode}\" start= auto obj= LocalSystem DisplayName= \"{name}\"");
        Sc($"description {name} \"Local policy host\"");
        // Restart after 5s on every failure; reset the failure counter daily.
        Sc($"failure {name} reset= 86400 actions= restart/5000/restart/5000/restart/5000");
        Sc($"failureflag {name} 1");
    }

    /// <summary>Starts the service; throws <see cref="BlockedByWindowsException"/> if Windows application control refuses it.</summary>
    public static void Start(string name)
    {
        try { Sc($"start {name}"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("1056")) { /* already running (started by the other service) */ }
        catch (InvalidOperationException ex) when (ex.Message.Contains("4551") || ex.Message.Contains("1260") || ex.Message.Contains("225"))
        { throw new BlockedByWindowsException(ex.Message); }
    }

    /// <summary>Locks the service DACL and registers it for Safe Mode. Only after it started fine.</summary>
    public static void Harden(string name)
    {
        Sc($"sdset {name} {HardenedSddl}");
        EnableInSafeMode(name);
    }

    /// <summary>Removes a not-yet-hardened service (rollback of a failed install).</summary>
    public static void RemoveUnhardened(string name)
    {
        Sc($"stop {name}", throwOnError: false);
        Sc($"delete {name}", throwOnError: false);
        DisableInSafeMode(name);
    }

    /// <summary>
    /// Services from earlier failed installs: they point into the install folder but are not the
    /// current ones. They may be hardened, so they are removed by a one-shot SYSTEM task that only
    /// runs Windows' own sc.exe.
    /// </summary>
    public static void RemoveLeftovers(IEnumerable<string> keep)
    {
        var keepSet = new HashSet<string>(keep.Where(k => k.Length > 0), StringComparer.OrdinalIgnoreCase);
        var leftovers = new List<string>();
        using (var services = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services"))
        {
            foreach (var n in services?.GetSubKeyNames() ?? [])
            {
                if (keepSet.Contains(n)) continue;
                try
                {
                    using var k = services!.OpenSubKey(n);
                    if (k?.GetValue("ImagePath") is string img && img.Contains(Paths.InstallDir, StringComparison.OrdinalIgnoreCase))
                        leftovers.Add(n);
                }
                catch (System.Security.SecurityException) { }
            }
        }
        if (leftovers.Count == 0) return;
        var dir = Path.Combine(Path.GetTempPath(), "GuardianCleanup-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var script = Path.Combine(dir, "cleanup.cmd");
        File.WriteAllLines(script, leftovers.SelectMany(n => new[]
        {
            $"sc.exe sdset {n} \"{DefaultSddl}\"",
            $"sc.exe failure {n} reset= 0 actions= \"\"",
            $"sc.exe stop {n}",
            $"sc.exe delete {n}",
        }).Append($"echo ok> \"{Path.Combine(dir, "done")}\""));
        const string task = "GuardianCleanupOnce";
        try
        {
            Run("schtasks.exe", $"/Create /F /TN {task} /RU SYSTEM /RL HIGHEST /SC ONCE /ST 00:00 /TR \"cmd.exe /c \\\"{script}\\\"\"");
            Run("schtasks.exe", $"/Run /TN {task}");
            for (var i = 0; i < 40 && !File.Exists(Path.Combine(dir, "done")); i++) Thread.Sleep(250);
        }
        finally
        {
            Run("schtasks.exe", $"/Delete /F /TN {task}");
            foreach (var n in leftovers) try { DisableInSafeMode(n); } catch { }
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private static void Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit();
    }

    /// <summary>Lets the service start in Safe Mode too (Minimal and Network), so booting into
    /// Safe Mode is not an instant way around the policy.</summary>
    public static void EnableInSafeMode(string name)
    {
        foreach (var mode in new[] { "Minimal", "Network" })
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey($@"SYSTEM\CurrentControlSet\Control\SafeBoot\{mode}\{name}");
            k.SetValue("", "Service");
        }
    }

    public static void DisableInSafeMode(string name)
    {
        foreach (var mode in new[] { "Minimal", "Network" })
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree($@"SYSTEM\CurrentControlSet\Control\SafeBoot\{mode}\{name}", false);
    }

    /// <summary>Starts a service if it is stopped. Returns true if a start was issued.</summary>
    public static bool EnsureRunning(string name)
    {
        using var sc = new System.ServiceProcess.ServiceController(name);
        try
        {
            if (sc.Status is System.ServiceProcess.ServiceControllerStatus.Stopped or System.ServiceProcess.ServiceControllerStatus.Paused)
            {
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Paused) sc.Continue(); else sc.Start();
                return true;
            }
        }
        catch (InvalidOperationException) { /* service missing or start refused */ }
        return false;
    }

    /// <summary>Must run as SYSTEM (i.e. inside the service) to be able to restore the DACL.</summary>
    public static void Unharden(string name) => Sc($"sdset {name} {DefaultSddl}");

    public static void DisableRecovery(string name)
    {
        DisableInSafeMode(name);
        DisableRecoveryActions(name);
    }

    private static void DisableRecoveryActions(string name) => Sc($"failure {name} reset= 0 actions= \"\"");

    /// <summary>Called by the CLI after the service has unhardened itself.</summary>
    public static void StopAndDelete(string name)
    {
        Sc($"stop {name}", throwOnError: false);
        Sc($"delete {name}");
    }

    private static void Sc(string args, bool throwOnError = true)
    {
        using var p = Process.Start(new ProcessStartInfo("sc.exe", args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        })!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (throwOnError && p.ExitCode != 0)
            throw new InvalidOperationException($"sc {args.Split(' ')[0]} failed ({p.ExitCode}): {output.Trim()}");
    }
}

/// <summary>Windows application control (Smart App Control / WDAC) or the antivirus refused to start the service.</summary>
public sealed class BlockedByWindowsException(string detail) : Exception("Blocked by Windows: " + detail);
