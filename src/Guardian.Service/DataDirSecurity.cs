using System.Security.AccessControl;
using System.Security.Principal;

namespace Guardian.Service;

/// <summary>Config folder: SYSTEM full control, Administrators read-only, no inheritance.
/// Changes therefore have to go through the password-checked service.</summary>
public static class DataDirSecurity
{
    /// <param name="usersCanRun">Program folder: every user may read and run the exe (the app window,
    /// the uninstaller started from Settings). Config folder: no access for normal users.</param>
    public static void Lock(string dir, bool usersCanRun = false)
    {
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        if (usersCanRun)
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).SetAccessControl(sec);
    }

    /// <summary>Back to normal inherited permissions so an administrator can delete the folder.</summary>
    public static void Unlock(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(dir).SetAccessControl(sec);
    }

    /// <summary>
    /// Makes a folder left locked by an earlier (failed or interrupted) install writable again for
    /// the elevated installer. Takes ownership first if needed (administrators always may).
    /// </summary>
    public static void ReclaimForInstall(string dir)
    {
        if (!Directory.Exists(dir)) return;
        try { Unlock(dir); return; }
        catch (UnauthorizedAccessException) { }
        catch (InvalidOperationException) { }
        using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("takeown.exe", $"/F \"{dir}\" /R /A /D Y")
               { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!)
        { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(); }
        Unlock(dir);
    }
}
