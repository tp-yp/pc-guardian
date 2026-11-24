namespace Guardian.Core.Network;

/// <summary>
/// "Known Wi-Fi networks only" uses Windows' own network filters (netsh wlan add filter):
/// every known network name gets an allow filter, and everything else is denied.
/// Windows then hides and refuses other networks, for every account.
/// </summary>
public static class WifiFilters
{
    /// <summary>Names netsh can take safely as one quoted argument.</summary>
    public static bool IsValidSsid(string ssid) =>
        ssid.Length is > 0 and <= 32 && !ssid.Any(c => char.IsControl(c) || c == '"');

    public static List<string> ApplyCommands(IEnumerable<string> allowed)
    {
        var cmds = allowed.Where(IsValidSsid).Distinct()
            .Select(s => $"wlan add filter permission=allow ssid=\"{s}\" networktype=infrastructure").ToList();
        cmds.Add("wlan add filter permission=denyall networktype=infrastructure");
        cmds.Add("wlan add filter permission=denyall networktype=adhoc");
        return cmds;
    }

    public static List<string> RemoveCommands(IEnumerable<string> allowed)
    {
        var cmds = new List<string>
        {
            "wlan delete filter permission=denyall networktype=infrastructure",
            "wlan delete filter permission=denyall networktype=adhoc",
        };
        cmds.AddRange(allowed.Where(IsValidSsid).Distinct()
            .Select(s => $"wlan delete filter permission=allow ssid=\"{s}\" networktype=infrastructure"));
        return cmds;
    }
}
