using System.Diagnostics;
using System.Management;
using System.Text.Json;
using System.Xml.Linq;
using Guardian.Core.Apps;
using Microsoft.Win32;

namespace Guardian.Service.Enforcers;

/// <summary>
/// Lists the programs a person would recognise: Start-menu shortcuts (all users), "Installed apps"
/// (uninstall registry, machine and every loaded user) and Store apps. Cached for 10 minutes.
/// </summary>
public sealed class AppCatalogScanner(ILogger<AppCatalogScanner> log)
{
    private List<CatalogApp> _cache = [];
    private DateTime _cachedAt = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<List<CatalogApp>> GetAsync(bool refresh = false)
    {
        await _lock.WaitAsync();
        try
        {
            if (refresh || DateTime.UtcNow - _cachedAt > TimeSpan.FromMinutes(10))
            {
                var found = new List<CatalogApp>();
                Collect(found, StartMenu, "start menu");
                Collect(found, Registry_, "installed apps");
                Collect(found, StoreApps, "store apps");
                _cache = AppCatalog.Merge(found);
                _cachedAt = DateTime.UtcNow;
            }
            return _cache;
        }
        finally { _lock.Release(); }
    }

    private void Collect(List<CatalogApp> into, Func<IEnumerable<CatalogApp>> source, string what)
    {
        try { into.AddRange(source()); }
        catch (Exception ex) { log.LogWarning(ex, "Listing {What} failed", what); }
    }

    private static IEnumerable<string> StartMenuFolders()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        var users = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "Users");
        foreach (var u in SafeDirs(users))
            yield return Path.Combine(u, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs");
    }

    private static IEnumerable<CatalogApp> StartMenu()
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        foreach (var folder in StartMenuFolders().Where(Directory.Exists))
            foreach (var lnk in SafeFiles(folder, "*.lnk"))
            {
                string target;
                try { target = (string)shell.CreateShortcut(lnk).TargetPath; } catch { continue; }
                if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                    yield return new CatalogApp(Path.GetFileNameWithoutExtension(lnk), target, AppSource.StartMenu);
            }
    }

    private static IEnumerable<CatalogApp> Registry_()
    {
        const string sub = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var roots = new List<RegistryKey?>
        {
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(sub),
            RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(sub),
        };
        foreach (var sid in Registry.Users.GetSubKeyNames().Where(n => n.StartsWith("S-1-5-21") && !n.EndsWith("_Classes")))
            roots.Add(Registry.Users.OpenSubKey(sid + "\\" + sub));
        foreach (var root in roots.Where(r => r != null))
            using (root)
                foreach (var name in root!.GetSubKeyNames())
                {
                    using var k = root.OpenSubKey(name);
                    if (k?.GetValue("DisplayName") is not string display || k.GetValue("SystemComponent") is 1) continue;
                    var exe = ExeFromIcon(k.GetValue("DisplayIcon") as string);
                    if (exe != null) yield return new CatalogApp(display, exe, AppSource.InstalledApps);
                }
    }

    private static string? ExeFromIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        var p = icon.Trim().Trim('"');
        var comma = p.LastIndexOf(',');
        if (comma > 2 && !p[comma..].Contains('\\')) p = p[..comma].Trim().Trim('"');
        p = Environment.ExpandEnvironmentVariables(p);
        return p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p) ? p : null;
    }

    private static IEnumerable<CatalogApp> StoreApps()
    {
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -Command \"Get-AppxPackage -AllUsers | Where-Object { -not $_.IsFramework -and $_.SignatureKind -ne 'System' -and $_.InstallLocation } | Select-Object Name,InstallLocation | ConvertTo-Json -Compress\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        using var p = Process.Start(psi)!;
        var json = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);
        if (string.IsNullOrWhiteSpace(json)) yield break;
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        foreach (var item in items)
        {
            var dir = item.GetProperty("InstallLocation").GetString();
            var pkg = item.GetProperty("Name").GetString() ?? "";
            if (dir is null) continue;
            var manifest = Path.Combine(dir, "AppxManifest.xml");
            if (!File.Exists(manifest)) continue;
            XDocument x;
            try { x = XDocument.Load(manifest); } catch { continue; }
            var display = x.Descendants().FirstOrDefault(e => e.Name.LocalName == "DisplayName")?.Value ?? "";
            if (display.Length == 0 || display.StartsWith("ms-resource:")) display = pkg.Split('.').Last();
            foreach (var app in x.Descendants().Where(e => e.Name.LocalName == "Application"))
            {
                var exe = app.Attribute("Executable")?.Value;
                if (exe != null && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    yield return new CatalogApp(display, Path.Combine(dir, exe), AppSource.Store);
            }
        }
    }

    /// <summary>Enabled local Windows accounts (for the Users screen).</summary>
    public static List<string> LocalAccounts()
    {
        var list = new List<string>();
        using var s = new ManagementObjectSearcher("SELECT Name, Disabled FROM Win32_UserAccount WHERE LocalAccount=True");
        foreach (ManagementObject o in s.Get())
            using (o)
                if (o["Disabled"] is false && o["Name"] is string n) list.Add(n);
        return list.Where(n => n is not ("DefaultAccount" or "WDAGUtilityAccount" or "Guest" or "אורח")).OrderBy(n => n).ToList();
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); } catch { return []; }
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern, new System.IO.EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }).ToList(); }
        catch { return []; }
    }
}
