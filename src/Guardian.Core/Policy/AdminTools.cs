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
        "cmd.exe", "powershell.exe", "powershell_ise.exe", "pwsh.exe", "wt.exe", "windowsterminal.exe", "openconsole.exe",
        "regedit.exe", "regedt32.exe", "reg.exe", "mmc.exe", "sc.exe", "takeown.exe", "icacls.exe", "cacls.exe",
        "net.exe", "net1.exe", "netsh.exe", "bcdedit.exe", "reagentc.exe", "msconfig.exe", "wmic.exe", "schtasks.exe",
        "taskkill.exe", "wsl.exe", "wslhost.exe", "bash.exe", "cscript.exe", "wscript.exe", "mshta.exe",
        "rstrui.exe", "systemreset.exe", "gpedit.exe", "secpol.exe", "runas.exe", "sdbinst.exe", "diskpart.exe",
    };

    public static bool Is(string path, string? originalName)
    {
        var file = path.Replace('/', '\\');
        file = file[(file.LastIndexOf('\\') + 1)..];
        if (Names.Contains(file)) return true;
        return originalName is { Length: > 0 } o && Names.Contains(AppPolicyEvaluator.ExeName(o));
    }
}
