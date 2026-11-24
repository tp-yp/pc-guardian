using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace Guardian.Service.Enforcers;

/// <summary>
/// Points Windows (WinINET for browsers and most apps, WinHTTP for services) at the local proxy,
/// machine-wide, and locks the setting so users cannot change it. Firefox is told to use the system
/// proxy and to trust the Windows root store.
/// </summary>
internal static class SystemProxySettings
{
    private const string Server = "127.0.0.1:8899";
    private const string Bypass = "<local>;127.0.0.1;localhost";
    private const string InetSettings = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string InetPolicy = @"SOFTWARE\Policies\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string IeLock = @"SOFTWARE\Policies\Microsoft\Internet Explorer\Control Panel";
    private const string Firefox = @"SOFTWARE\Policies\Mozilla\Firefox";

    public static bool IsApplied()
    {
        using var k = Registry.LocalMachine.OpenSubKey(InetSettings);
        using var p = Registry.LocalMachine.OpenSubKey(InetPolicy);
        return Equals(k?.GetValue("ProxyServer"), Server) && Equals(p?.GetValue("ProxySettingsPerUser"), 0);
    }

    public static void Apply()
    {
        using (var p = Registry.LocalMachine.CreateSubKey(InetPolicy)) p.SetValue("ProxySettingsPerUser", 0, RegistryValueKind.DWord);
        using (var k = Registry.LocalMachine.CreateSubKey(InetSettings))
        {
            k.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            k.SetValue("ProxyServer", Server);
            k.SetValue("ProxyOverride", Bypass);
        }
        using (var c = Registry.LocalMachine.CreateSubKey(InetSettings + @"\Connections"))
        {
            var blob = ConnectionBlob(Server, Bypass);
            c.SetValue("DefaultConnectionSettings", blob, RegistryValueKind.Binary);
            c.SetValue("SavedLegacySettings", blob, RegistryValueKind.Binary);
        }
        using (var l = Registry.LocalMachine.CreateSubKey(IeLock))
        {
            l.SetValue("Proxy", 1, RegistryValueKind.DWord);
            l.SetValue("AutoConfig", 1, RegistryValueKind.DWord);
        }
        using (var f = Registry.LocalMachine.CreateSubKey(Firefox + @"\Proxy"))
        {
            f.SetValue("Mode", "system");
            f.SetValue("Locked", 1, RegistryValueKind.DWord);
        }
        using (var f = Registry.LocalMachine.CreateSubKey(Firefox + @"\Certificates")) f.SetValue("ImportEnterpriseRoots", 1, RegistryValueKind.DWord);
        Run("netsh.exe", $"winhttp set proxy proxy-server=\"{Server}\" bypass-list=\"{Bypass}\"");
    }

    /// <summary>"Block VPN and proxies" without full-URL filtering: no proxy for anyone, and the setting locked,
    /// so a proxy cannot be set by hand to get around the DNS filter.</summary>
    public static bool IsDirectApplied()
    {
        using var k = Registry.LocalMachine.OpenSubKey(InetSettings);
        using var p = Registry.LocalMachine.OpenSubKey(InetPolicy);
        using var l = Registry.LocalMachine.OpenSubKey(IeLock);
        return Equals(p?.GetValue("ProxySettingsPerUser"), 0) && Equals(k?.GetValue("ProxyEnable"), 0) && k?.GetValue("ProxyServer") is null
               && Equals(l?.GetValue("Proxy"), 1);
    }

    public static void ApplyDirect()
    {
        using (var p = Registry.LocalMachine.CreateSubKey(InetPolicy)) p.SetValue("ProxySettingsPerUser", 0, RegistryValueKind.DWord);
        using (var k = Registry.LocalMachine.CreateSubKey(InetSettings))
        {
            k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            k.DeleteValue("ProxyServer", false);
            k.DeleteValue("ProxyOverride", false);
            k.DeleteValue("AutoConfigURL", false);
        }
        using (var c = Registry.LocalMachine.CreateSubKey(InetSettings + @"\Connections"))
        {
            var blob = DirectBlob();
            c.SetValue("DefaultConnectionSettings", blob, RegistryValueKind.Binary);
            c.SetValue("SavedLegacySettings", blob, RegistryValueKind.Binary);
        }
        using (var l = Registry.LocalMachine.CreateSubKey(IeLock))
        {
            l.SetValue("Proxy", 1, RegistryValueKind.DWord);
            l.SetValue("AutoConfig", 1, RegistryValueKind.DWord);
        }
        using (var f = Registry.LocalMachine.CreateSubKey(Firefox + @"\Proxy"))
        {
            f.SetValue("Mode", "system");
            f.SetValue("Locked", 1, RegistryValueKind.DWord);
        }
        Run("netsh.exe", "winhttp reset proxy");
    }

    private static byte[] DirectBlob()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0x46);
        w.Write(Environment.TickCount & 0x7FFFFFFF);
        w.Write(0x01); // direct connection only
        w.Write(0); w.Write(0); w.Write(0);
        w.Write(new byte[32]);
        return ms.ToArray();
    }

    public static void Remove()
    {
        using (var p = Registry.LocalMachine.OpenSubKey(InetPolicy, true)) p?.DeleteValue("ProxySettingsPerUser", false);
        using (var k = Registry.LocalMachine.OpenSubKey(InetSettings, true))
        {
            k?.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            k?.DeleteValue("ProxyServer", false);
            k?.DeleteValue("ProxyOverride", false);
        }
        using (var c = Registry.LocalMachine.OpenSubKey(InetSettings + @"\Connections", true))
        {
            c?.DeleteValue("DefaultConnectionSettings", false);
            c?.DeleteValue("SavedLegacySettings", false);
        }
        using (var l = Registry.LocalMachine.OpenSubKey(IeLock, true))
        {
            l?.DeleteValue("Proxy", false);
            l?.DeleteValue("AutoConfig", false);
        }
        Registry.LocalMachine.DeleteSubKeyTree(Firefox + @"\Proxy", false);
        Registry.LocalMachine.DeleteSubKeyTree(Firefox + @"\Certificates", false);
        Run("netsh.exe", "winhttp reset proxy");
    }

    /// <summary>WinINET DefaultConnectionSettings: version, counter, flags (direct|proxy), proxy, bypass, PAC url.</summary>
    private static byte[] ConnectionBlob(string server, string bypass)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0x46);
        w.Write(Environment.TickCount & 0x7FFFFFFF);
        w.Write(0x03);
        w.Write(server.Length); w.Write(Encoding.ASCII.GetBytes(server));
        w.Write(bypass.Length); w.Write(Encoding.ASCII.GetBytes(bypass));
        w.Write(0);
        w.Write(new byte[32]);
        return ms.ToArray();
    }

    private static void Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        p.WaitForExit(10_000);
    }
}
