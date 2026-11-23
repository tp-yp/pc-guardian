namespace Guardian.Service;

public static class Paths
{
    /// <summary>ProgramData folder; ACL'd to SYSTEM + Administrators by the installer.</summary>
    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GuardianData");

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GuardianHost");
}
