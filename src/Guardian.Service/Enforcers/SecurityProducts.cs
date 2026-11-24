using System.Management;

namespace Guardian.Service.Enforcers;

/// <summary>
/// Firewalls of other security products (ESET, Kaspersky, Norton, Bitdefender...) as Windows Security
/// Center lists them. When one is on, it usually turns the Windows Firewall off and takes its place, so
/// Guardian's firewall rules (no-internet programs, filtered-line isolation, full-URL filtering) stop working.
/// </summary>
public static class SecurityProducts
{
    private static List<string> _cache = [];
    private static DateTime _at = DateTime.MinValue;

    public static List<string> ActiveThirdPartyFirewalls()
    {
        if (DateTime.UtcNow - _at < TimeSpan.FromMinutes(10)) return _cache;
        var found = new List<string>();
        try
        {
            using var s = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM FirewallProduct");
            foreach (var o in s.Get())
            {
                using (o)
                {
                    var name = o["displayName"]?.ToString() ?? "";
                    var state = Convert.ToUInt32(o["productState"] ?? 0u);
                    // Second byte: 0x10/0x11 = on, 0x00/0x01 = off.
                    var on = ((state >> 8) & 0xF0) == 0x10;
                    if (on && name.Length > 0 && !name.Contains("Windows", StringComparison.OrdinalIgnoreCase)) found.Add(name);
                }
            }
        }
        catch (Exception) { /* Security Center not available (e.g. Windows Server) */ }
        _cache = found;
        _at = DateTime.UtcNow;
        return found;
    }
}
