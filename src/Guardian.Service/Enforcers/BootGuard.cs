using System.Diagnostics;
using System.Text.Json;
using Guardian.Core.Boot;
using Guardian.Core.Config;
using Microsoft.Win32;

namespace Guardian.Service.Enforcers;

/// <summary>
/// "Boot lockdown" (only when the owner turned it on, after an explicit warning):
///  * the recovery environment (WinRE) stays disabled: no "Advanced startup" / Shift+Restart,
///    no automatic repair, no recovery command prompt from the boot menu;
///  * a pending restart into Safe Mode (msconfig "Safe boot", bcdedit safeboot) is removed;
///  * automatic recovery after failed boots is off, and the F8 menu (legacy boot menu) stays off.
/// The state before the lockdown is saved and put back exactly when it is turned off or on uninstall.
/// Booting from an external disk is not blocked here; that needs a BIOS/UEFI password.
/// </summary>
public sealed class BootGuard(ILogger<BootGuard> log, AccessLog access, IHostApplicationLifetime life)
{
    private static string StatePath => Path.Combine(Paths.DataDir, "boot-original.json");
    private static string ReAgentXml => Path.Combine(Environment.SystemDirectory, "Recovery", "ReAgent.xml");
    private DateTime _nextWinReCheck = DateTime.MinValue;
    private volatile bool _on;
    private bool _hooked;

    private sealed record Original(bool WinReEnabled, bool RecoveryEnabled, bool LegacyMenu);

    public void Tick(GuardianConfig cfg)
    {
        _on = cfg.Boot.Lockdown;
        if (!_hooked)
        {
            // Last check while Windows shuts down: a Safe Mode request set seconds before a restart is removed too.
            life.ApplicationStopping.Register(() => { if (_on && File.Exists(Paths.DataDir + "\\config.bin")) try { EnforceBcd(); } catch { } });
            _hooked = true;
        }
        if (!cfg.Boot.Lockdown)
        {
            if (File.Exists(StatePath)) { Restore(); log.LogInformation("Boot lockdown turned off; original boot settings restored"); }
            return;
        }
        if (!File.Exists(StatePath)) SaveOriginal();
        EnforceBcd();
        if (DateTime.UtcNow >= _nextWinReCheck)
        {
            _nextWinReCheck = DateTime.UtcNow.AddSeconds(60);
            if (BootState.WinReEnabled(ReadReAgent()) == true)
            {
                Run("reagentc.exe", "/disable");
                access.Add("boot", "סביבת השחזור הופעלה ונכבתה שוב", allowed: false);
                log.LogWarning("Recovery environment was enabled; disabled again");
            }
        }
    }

    private void SaveOriginal()
    {
        var current = CurrentLoader();
        var o = new Original(
            WinReEnabled: BootState.WinReEnabled(ReadReAgent()) == true,
            RecoveryEnabled: current is null || ReadBool(current, BootState.AutoRecoveryEnabled) != false,
            LegacyMenu: current != null && ReadInt(current, BootState.BootMenuPolicy) == 0);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(o));
        log.LogInformation("Boot lockdown on. Saved original state: {State}", o);
    }

    private void EnforceBcd()
    {
        var current = CurrentLoader();
        if (current is null) { log.LogDebug("BCD not readable"); return; }
        var safe = HasElement(current, BootState.SafeBoot);
        var cmds = BootState.LockdownCommands(safe, HasElement(current, BootState.SafeBootAlternateShell),
            ReadBool(current, BootState.AutoRecoveryEnabled), ReadInt(current, BootState.BootMenuPolicy));
        foreach (var c in cmds) Run("bcdedit.exe", c);
        if (safe)
        {
            access.Add("boot", "בקשה להפעלה מחדש למצב בטוח בוטלה", allowed: false);
            log.LogWarning("Safe Mode boot request removed");
        }
    }

    /// <summary>Puts the boot settings back as they were before the lockdown (also used on uninstall).</summary>
    public static void Restore()
    {
        Original? o = null;
        try { o = JsonSerializer.Deserialize<Original>(File.ReadAllText(StatePath)); } catch { /* no saved state */ }
        if (o is null) return;
        if (o.RecoveryEnabled) Run("bcdedit.exe", "/set {current} recoveryenabled Yes");
        if (o.LegacyMenu) Run("bcdedit.exe", "/set {current} bootmenupolicy Legacy");
        if (o.WinReEnabled) Run("reagentc.exe", "/enable");
        File.Delete(StatePath);
    }

    // ---- BCD store, read through the registry (HKLM\BCD00000000) so it does not depend on the UI language ----
    private static RegistryKey? Objects() => Registry.LocalMachine.OpenSubKey(@"BCD00000000\Objects");

    private static string? CurrentLoader()
    {
        using var objs = Objects();
        using var el = objs?.OpenSubKey($@"{BootState.BootMgrId}\Elements\{BootState.BootMgrDefault}");
        return el?.GetValue("Element") as string;
    }

    private static bool HasElement(string obj, string type)
    {
        using var objs = Objects();
        using var k = objs?.OpenSubKey($@"{obj}\Elements\{type}");
        return k != null;
    }

    private static bool? ReadBool(string obj, string type)
    {
        using var objs = Objects();
        using var k = objs?.OpenSubKey($@"{obj}\Elements\{type}");
        return k?.GetValue("Element") is byte[] { Length: > 0 } b ? b[0] != 0 : null;
    }

    private static long? ReadInt(string obj, string type)
    {
        using var objs = Objects();
        using var k = objs?.OpenSubKey($@"{obj}\Elements\{type}");
        return k?.GetValue("Element") is byte[] { Length: >= 8 } b ? BitConverter.ToInt64(b, 0) : null;
    }

    private static string? ReadReAgent()
    {
        try { return File.ReadAllText(ReAgentXml); } catch { return null; }
    }

    private static void Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit(30_000);
    }
}
