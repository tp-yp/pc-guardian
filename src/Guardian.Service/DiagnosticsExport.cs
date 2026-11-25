using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Guardian.Core.Config;
using Guardian.Core.Storage;
using Guardian.Service.Enforcers;

namespace Guardian.Service;

/// <summary>
/// "Export logs": one zip with what is needed to look into a problem. The service's log files, the
/// recent activity list, the settings without passwords, and basic facts about this computer.
/// </summary>
public static class DiagnosticsExport
{
    public static byte[] Build(GuardianConfig cfg, AccessLog access)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Text(zip, "info.txt", Info());
            Text(zip, "settings.json", ConfigEditing.Export(cfg));
            Text(zip, "activity.json", JsonSerializer.Serialize(access.Recent(5000), EncryptedConfigStore.Json));
            if (Directory.Exists(FileLogProvider.Dir))
                foreach (var f in Directory.GetFiles(FileLogProvider.Dir, "geder-*.log").OrderBy(f => f))
                {
                    try
                    {
                        // Shared read: the service may be writing to today's file right now.
                        using var src = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var dst = zip.CreateEntry("logs/" + Path.GetFileName(f)).Open();
                        src.CopyTo(dst);
                    }
                    catch (IOException) { /* skip a file that cannot be read */ }
                }
        }
        return ms.ToArray();
    }

    private static string Info()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Geder {typeof(DiagnosticsExport).Assembly.GetName().Version}");
        sb.AppendLine($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ({TimeZoneInfo.Local.Id})");
        sb.AppendLine($"Windows: {Environment.OSVersion.VersionString}");
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            sb.AppendLine($"Build: {k?.GetValue("DisplayVersion")} {k?.GetValue("CurrentBuild")}.{k?.GetValue("UBR")} {k?.GetValue("EditionID")}");
        }
        catch (Exception) { /* not important */ }
        sb.AppendLine($"Program: {Environment.ProcessPath}");
        return sb.ToString();
    }

    private static void Text(ZipArchive zip, string name, string text)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write(text);
    }
}
