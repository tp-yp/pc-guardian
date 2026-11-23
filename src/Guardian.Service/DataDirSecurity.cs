using System.Security.AccessControl;
using System.Security.Principal;

namespace Guardian.Service;

/// <summary>Config folder: SYSTEM full control, Administrators read-only, no inheritance.
/// Changes therefore have to go through the password-checked service.</summary>
public static class DataDirSecurity
{
    public static void Lock(string dir)
    {
        var sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
        sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
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
}
