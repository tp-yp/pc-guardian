namespace Guardian.Core.Policy;

/// <summary>
/// "Block admin tools": the Windows tools almost every set of bypass instructions goes through
/// (command line, PowerShell, registry editor, management consoles, task scheduler, service and
/// permission commands, System Restore and Reset). Matched by file name and by the name written
/// inside the file, so a renamed or copied tool is caught too. Task Manager and Settings stay open.
/// </summary>
public static class AdminTools
{
    public static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "powershell_ise.exe", "pwsh.exe",
        "regedit.exe", "regedt32.exe", "reg.exe", "mmc.exe", "sc.exe", "takeown.exe", "icacls.exe", "cacls.exe",
        "net.exe", "net1.exe", "netsh.exe", "bcdedit.exe", "reagentc.exe", "msconfig.exe", "wmic.exe", "schtasks.exe",
        "taskkill.exe", "wsl.exe", "wslhost.exe", "bash.exe", "cscript.exe", "wscript.exe", "mshta.exe",
        "rstrui.exe", "systemreset.exe", "gpedit.exe", "secpol.exe", "runas.exe", "sdbinst.exe", "diskpart.exe",
        // Windows Security (where Defender and the firewall are turned off), accounts and recovery tools.
        "sechealthui.exe", "netplwiz.exe", "useraccountcontrolsettings.exe", "recoverydrive.exe", "dism.exe",
    };

    /// <summary>
    /// Programs through which a person starts things by hand: File Explorer, Start, Run, search, Task Manager
    /// ("Run new task"), terminals and scheduled tasks. A tool started by one of these (or by another tool, or by
    /// a parent that is already gone) is blocked. A tool that an ordinary installed program runs in the
    /// background (installers, updaters) is left alone, so those programs keep working.
    /// </summary>
    public static readonly HashSet<string> Launchers = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "taskmgr.exe", "startmenuexperiencehost.exe", "searchhost.exe", "searchapp.exe", "sihost.exe",
        "openwith.exe", "rundll32.exe", "windowsterminal.exe", "openconsole.exe", "wt.exe", "conhost.exe",
        "svchost.exe", "taskhostw.exe", "runtimebroker.exe", "dllhost.exe", "consent.exe", "msedge.exe", "chrome.exe", "firefox.exe",
        // Windows screens that open the tools with a button: System Restore from "System Protection",
        // "Reset this PC" from Settings, and the Control Panel.
        "systempropertiesprotection.exe", "systempropertiesadvanced.exe", "systemsettings.exe", "systemsettingsadminflows.exe", "control.exe",
    };

    /// <summary>Whether a tool started by <paramref name="parentPath"/> (null = unknown / gone) counts as started by hand.</summary>
    public static bool StartedByHand(string? parentPath)
    {
        if (string.IsNullOrEmpty(parentPath)) return true;
        var file = parentPath.Replace('/', '\\');
        file = file[(file.LastIndexOf('\\') + 1)..];
        return Launchers.Contains(file) || Names.Contains(file);
    }

    public static bool Is(string path, string? originalName)
    {
        var file = path.Replace('/', '\\');
        file = file[(file.LastIndexOf('\\') + 1)..];
        if (Names.Contains(file)) return true;
        return originalName is { Length: > 0 } o && Names.Contains(AppPolicyEvaluator.ExeName(o));
    }
}
