namespace Guardian.Core.Policy;

/// <summary>
/// Parts of Windows that must never be locked or cut off from the network, because the desktop,
/// sign-in or networking stop working without them. Only the real files in the Windows folder (and a few
/// fixed Microsoft folders) are protected, so a renamed copy elsewhere ("explorer.exe" in Downloads) is not.
/// Tools people may want to block (cmd, PowerShell, regedit, Task Manager, Settings) are deliberately not here.
/// </summary>
public static class ProtectedApps
{
    public static readonly HashSet<string> SystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "svchost.exe", "winlogon.exe", "wininit.exe", "csrss.exe", "smss.exe", "lsass.exe", "lsaiso.exe",
        "services.exe", "dwm.exe", "sihost.exe", "taskhostw.exe", "ctfmon.exe", "fontdrvhost.exe", "runtimebroker.exe",
        "userinit.exe", "logonui.exe", "consent.exe", "dllhost.exe", "conhost.exe", "searchhost.exe", "searchindexer.exe",
        "searchprotocolhost.exe", "searchfilterhost.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe",
        "textinputhost.exe", "applicationframehost.exe", "lockapp.exe", "audiodg.exe", "spoolsv.exe", "wmiprvse.exe",
        "wudfhost.exe", "smartscreen.exe", "backgroundtaskhost.exe", "mousocoreworker.exe", "tiworker.exe",
        "trustedinstaller.exe", "werfault.exe", "wlanext.exe", "dashost.exe", "unsecapp.exe", "sppsvc.exe",
        "securityhealthservice.exe", "securityhealthsystray.exe", "securityhealthhost.exe", "registry", "memcompression",
        "credentialuibroker.exe", "useroobebroker.exe", "systemsettingsbroker.exe", "shellhost.exe", "widgets.exe",
    };

    /// <summary>Folders whose programs are all part of Windows itself (relative to the Windows folder) or of the
    /// pieces Guardian and Windows security need (anywhere on the path).</summary>
    private static readonly string[] WindowsSubFolders = [@"SystemApps\", @"WinSxS\", @"servicing\"];
    private static readonly string[] AnywhereFolders = [@"\Microsoft\EdgeWebView\", @"\Windows Defender\", @"\Microsoft\Windows Defender\"];

    /// <param name="windowsDir">The Windows folder, normally C:\Windows.</param>
    /// <param name="guardianDir">Guardian's own program folder, if known.</param>
    public static bool IsProtected(string path, string windowsDir = @"C:\Windows", string? guardianDir = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var p = path.Replace('/', '\\').Trim().Trim('"');
        var win = windowsDir.Replace('/', '\\').TrimEnd('\\') + "\\";
        if (guardianDir is { Length: > 0 } g && p.StartsWith(g.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        if (AnywhereFolders.Any(f => p.Contains(f, StringComparison.OrdinalIgnoreCase))) return true;
        if (!p.StartsWith(win, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = p[win.Length..];
        if (WindowsSubFolders.Any(f => rest.StartsWith(f, StringComparison.OrdinalIgnoreCase))) return true;
        var name = rest[(rest.LastIndexOf('\\') + 1)..];
        return SystemNames.Contains(name);
    }

    /// <summary>A bare file name ("explorer.exe") that names a protected part of Windows. Locking it by name
    /// would only hit copies outside Windows, which is allowed, but it is almost always a mistake.</summary>
    public static bool IsProtectedName(string value) =>
        !value.Contains('\\') && !value.Contains('/') && SystemNames.Contains(value.Trim().Trim('"'));
}
