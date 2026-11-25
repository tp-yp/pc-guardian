using System.Security.Principal;
using Microsoft.Win32;

namespace Guardian.Service.Native;

/// <summary>
/// The profile folder of every local and Microsoft account on this computer (C:\Users\kid → "PC\kid"),
/// read from Windows' own profile list. A profile whose account no longer exists keeps its SID as the
/// owner, so its programs count as another account's for everybody. Refreshed every 5 minutes.
/// </summary>
internal static class UserProfiles
{
    private static readonly object Gate = new();
    private static Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
    private static DateTime _at = DateTime.MinValue;

    public static IReadOnlyDictionary<string, string> Current()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _at < TimeSpan.FromMinutes(5)) return _map;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var list = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");
                foreach (var sid in list?.GetSubKeyNames() ?? [])
                {
                    if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal) || sid.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) continue;
                    using var k = list!.OpenSubKey(sid);
                    if (k?.GetValue("ProfileImagePath") is not string dir || dir.Length < 4) continue;
                    dir = Environment.ExpandEnvironmentVariables(dir).TrimEnd('\\');
                    string owner;
                    try { owner = ((NTAccount)new SecurityIdentifier(sid).Translate(typeof(NTAccount))).Value; }
                    catch (Exception) { owner = sid; }
                    map[dir] = owner;
                }
            }
            catch (Exception) { /* keep the last good list */ if (_map.Count > 0) map = _map; }
            _map = map;
            _at = DateTime.UtcNow;
            return _map;
        }
    }
}
