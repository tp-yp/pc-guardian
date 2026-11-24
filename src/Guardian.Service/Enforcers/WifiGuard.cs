using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Guardian.Core.Config;
using Guardian.Core.Network;

namespace Guardian.Service.Enforcers;

/// <summary>
/// "Known Wi-Fi networks only" (whole computer). Uses Windows' own Wi-Fi filters: the saved networks
/// are allowed, all others are hidden and refused. The filters are re-applied every few minutes, so
/// deleting them by hand does not last. Turning the switch off (or uninstalling) removes them.
/// Wired connections and phone USB tethering are not Wi-Fi and are not covered.
/// </summary>
public sealed class WifiGuard(ILogger<WifiGuard> log, AccessLog access)
{
    private static string StatePath => Path.Combine(Paths.DataDir, "wifi-applied.json");
    private string _sig = "";
    private DateTime _next = DateTime.MinValue;

    public void Tick(GuardianConfig cfg)
    {
        if (!cfg.Wifi.KnownOnly)
        {
            if (File.Exists(StatePath)) { Restore(cfg); log.LogInformation("Known Wi-Fi only turned off; filters removed"); }
            _sig = "";
            return;
        }
        var sig = string.Join("\n", cfg.Wifi.Allowed);
        if (sig == _sig && DateTime.UtcNow < _next) return;
        // Networks taken off the list lose their allow filter.
        var before = ReadState();
        var gone = before.Where(s => !cfg.Wifi.Allowed.Contains(s)).ToList();
        foreach (var c in WifiFilters.RemoveCommands(gone).Where(c => c.Contains("permission=allow"))) Netsh(c);
        foreach (var c in WifiFilters.ApplyCommands(cfg.Wifi.Allowed)) Netsh(c);
        File.WriteAllText(StatePath, System.Text.Json.JsonSerializer.Serialize(cfg.Wifi.Allowed));
        if (sig != _sig) { log.LogInformation("Known Wi-Fi only: {Count} networks allowed", cfg.Wifi.Allowed.Count); access.Add("wifi", "רק רשתות Wi-Fi מוכרות: " + cfg.Wifi.Allowed.Count + " רשתות מותרות", allowed: false); }
        _sig = sig;
        _next = DateTime.UtcNow.AddMinutes(2);
    }

    /// <summary>Removes Guardian's Wi-Fi filters (switch off / uninstall).</summary>
    public static void Restore(GuardianConfig cfg)
    {
        foreach (var c in WifiFilters.RemoveCommands(cfg.Wifi.Allowed.Concat(ReadState()))) Netsh(c);
        try { File.Delete(StatePath); } catch { }
    }

    private static List<string> ReadState()
    {
        try { return File.Exists(StatePath) ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(File.ReadAllText(StatePath)) ?? [] : []; }
        catch { return []; }
    }

    private static void Netsh(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("netsh.exe", args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
        p.WaitForExit(15_000);
    }

    /// <summary>Network names (SSIDs) of the Wi-Fi networks saved on this computer. Null if there is no Wi-Fi.</summary>
    public static List<string>? SavedNetworks()
    {
        if (WlanOpenHandle(2, IntPtr.Zero, out _, out var h) != 0) return null;
        var result = new List<string>();
        try
        {
            if (WlanEnumInterfaces(h, IntPtr.Zero, out var ifList) != 0) return null;
            try
            {
                var n = Marshal.ReadInt32(ifList);
                if (n == 0) return null;
                for (var i = 0; i < n; i++)
                {
                    var ifPtr = ifList + 8 + i * 532; // WLAN_INTERFACE_INFO: GUID + WCHAR[256] + state
                    var guid = Marshal.PtrToStructure<Guid>(ifPtr);
                    if (WlanGetProfileList(h, ref guid, IntPtr.Zero, out var pList) != 0) continue;
                    try
                    {
                        var count = Marshal.ReadInt32(pList);
                        for (var j = 0; j < count; j++)
                        {
                            var name = Marshal.PtrToStringUni(pList + 8 + j * 516); // WLAN_PROFILE_INFO: WCHAR[256] + flags
                            if (string.IsNullOrEmpty(name)) continue;
                            var flags = 0u;
                            if (WlanGetProfile(h, ref guid, name, IntPtr.Zero, out var xmlPtr, ref flags, out _) != 0) continue;
                            try
                            {
                                var doc = XDocument.Parse(Marshal.PtrToStringUni(xmlPtr) ?? "");
                                var ssids = doc.Descendants().Where(e => e.Name.LocalName == "SSID")
                                    .Select(e => e.Elements().FirstOrDefault(x => x.Name.LocalName == "name")?.Value)
                                    .Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
                                result.AddRange(ssids.Count > 0 ? ssids : [name]);
                            }
                            catch { result.Add(name); }
                            finally { WlanFreeMemory(xmlPtr); }
                        }
                    }
                    finally { WlanFreeMemory(pList); }
                }
            }
            finally { WlanFreeMemory(ifList); }
        }
        finally { WlanCloseHandle(h, IntPtr.Zero); }
        return result.Where(WifiFilters.IsValidSsid).Distinct().ToList();
    }

    [DllImport("wlanapi.dll")] private static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern int WlanGetProfileList(IntPtr handle, ref Guid iface, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern int WlanGetProfile(IntPtr handle, ref Guid iface, string profile, IntPtr reserved, out IntPtr xml, ref uint flags, out uint access);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr p);
}
