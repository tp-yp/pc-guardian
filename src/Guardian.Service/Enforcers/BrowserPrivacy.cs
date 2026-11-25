using Microsoft.Win32;

namespace Guardian.Service.Enforcers;

/// <summary>
/// "No private browsing": the browsers' own policies that turn off incognito / InPrivate / private windows.
/// Re-applied every tick, removed when switched off and on removal.
/// </summary>
public static class BrowserPrivacy
{
    private static readonly (string Key, string Name, int Value)[] Policies =
    [
        (@"SOFTWARE\Policies\Microsoft\Edge", "InPrivateModeAvailability", 1),
        (@"SOFTWARE\Policies\Google\Chrome", "IncognitoModeAvailability", 1),
        (@"SOFTWARE\Policies\BraveSoftware\Brave", "IncognitoModeAvailability", 1),
        (@"SOFTWARE\Policies\BraveSoftware\Brave", "TorDisabled", 1),
        (@"SOFTWARE\Policies\Mozilla\Firefox", "DisablePrivateBrowsing", 1),
    ];

    private static bool? _last;

    public static void Apply(bool on)
    {
        // Switched off: the values are removed once (not every tick), so policies set by someone else stay.
        if (!on) { if (_last != false) Remove(); _last = false; return; }
        _last = true;
        foreach (var (key, name, value) in Policies)
        {
            using var k = Registry.LocalMachine.CreateSubKey(key);
            if (k.GetValue(name) is not int v || v != value) k.SetValue(name, value, RegistryValueKind.DWord);
        }
    }

    public static void Remove()
    {
        foreach (var (key, name, _) in Policies)
        {
            using var k = Registry.LocalMachine.OpenSubKey(key, writable: true);
            if (k?.GetValue(name) != null) k.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
