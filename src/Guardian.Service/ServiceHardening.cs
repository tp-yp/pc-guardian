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

    public static void Install(string name, string exePath)
    {
        Sc($"create {name} binPath= \"\\\"{exePath}\\\" run\" start= auto obj= LocalSystem DisplayName= \"{name}\"");
        Sc($"description {name} \"Local policy host\"");
        // Restart after 5s on every failure; reset the failure counter daily.
        Sc($"failure {name} reset= 86400 actions= restart/5000/restart/5000/restart/5000");
        Sc($"failureflag {name} 1");
        Sc($"sdset {name} {HardenedSddl}");
        Sc($"start {name}");
    }

    /// <summary>Must run as SYSTEM (i.e. inside the service) to be able to restore the DACL.</summary>
    public static void Unharden(string name) => Sc($"sdset {name} {DefaultSddl}");

    public static void DisableRecovery(string name) => Sc($"failure {name} reset= 0 actions= \"\"");

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
