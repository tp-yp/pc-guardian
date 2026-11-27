using System.Xml.Linq;

namespace Guardian.Core.Boot;

/// <summary>Language-independent readers for the boot settings Guardian watches.</summary>
public static class BootState
{
    /// <summary>
    /// Whether the recovery environment is enabled, from C:\Windows\System32\Recovery\ReAgent.xml
    /// (&lt;InstallState state="1"/&gt; = enabled). Null if the file is missing or unreadable.
    /// </summary>
    public static bool? WinReEnabled(string? reagentXml)
    {
        if (string.IsNullOrWhiteSpace(reagentXml)) return null;
        try
        {
            var state = XDocument.Parse(reagentXml).Descendants().FirstOrDefault(e => e.Name.LocalName == "InstallState")?.Attribute("state")?.Value;
            return state switch { "1" => true, "0" => false, _ => null };
        }
        catch (System.Xml.XmlException) { return null; }
    }

    /// <summary>
    /// Whether the recovery environment is on, from the output of "reagentc /info" (in any language): when it is on,
    /// the output names its location (\\?\GLOBALROOT\...) and a boot entry with a real id; when it is off, the id is
    /// all zeros. Null if the output says neither. This is what Windows itself goes by; ReAgent.xml can lag behind.
    /// </summary>
    public static bool? WinReEnabledFromInfo(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        if (output.Contains("GLOBALROOT", StringComparison.OrdinalIgnoreCase)) return true;
        var ids = System.Text.RegularExpressions.Regex.Matches(output, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        if (ids.Count == 0) return null;
        return ids.Any(m => m.Value.Trim('0', '-').Length > 0);
    }

    // BCD element types (stored under HKLM\BCD00000000\Objects\{id}\Elements\{type}).
    public const string SafeBoot = "25000080";               // safeboot Minimal/Network/DsRepair
    public const string SafeBootAlternateShell = "26000081";  // safebootalternateshell
    public const string AutoRecoveryEnabled = "16000009";     // recoveryenabled
    public const string BootMenuPolicy = "250000c2";
    public const string RecoverySequence = "14000008";       // {current} -> recovery environment loader (Advanced startup uses it)          // 0 = Legacy (F8 works), 1 = Standard
    public const string BootMgrDefault = "23000003";          // {bootmgr} default -> {current} id
    public const string BootMgrId = "{9dea862c-5cdd-4e70-acc1-f32b344d4795}";

    /// <summary>bcdedit commands that undo a pending Safe Mode / enforce the lockdown, given what was found.</summary>
    public static List<string> LockdownCommands(bool safeBootSet, bool altShellSet, bool? recoveryEnabled, long? menuPolicy)
    {
        var cmds = new List<string>();
        if (safeBootSet) cmds.Add("/deletevalue {current} safeboot");
        if (altShellSet) cmds.Add("/deletevalue {current} safebootalternateshell");
        if (recoveryEnabled != false) cmds.Add("/set {current} recoveryenabled No");
        if (menuPolicy == 0) cmds.Add("/set {current} bootmenupolicy Standard");
        return cmds;
    }
}
