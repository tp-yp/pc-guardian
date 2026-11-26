namespace Guardian.Core.Policy;

/// <summary>
/// Parts of Windows that live outside the Windows folder, allowed automatically in allow-list mode so the
/// computer keeps working: Windows' own Store apps (Calculator, Photos, Notepad, Snipping Tool, Windows
/// Security), shared Microsoft components (handwriting, spelling) and the WebView2 / Defender folders.
/// Ordinary programs, also Microsoft's (Edge, Office, Teams), are not included: they are allowed by hand.
/// </summary>
public static class SystemApps
{
    private static readonly string[] PackagePrefixes =
        ["Microsoft.Windows", "MicrosoftWindows.", "Microsoft.SecHealthUI", "Microsoft.Paint", "Microsoft.ScreenSketch",
         "Microsoft.WindowsNotepad", "Microsoft.UI.Xaml", "Microsoft.VCLibs", "Microsoft.NET.Native", "Microsoft.WindowsAppRuntime"];
    private static readonly string[] Folders = [@"\Common Files\microsoft shared\", @"\Microsoft\EdgeWebView\", @"\Windows Defender\", @"\Microsoft\Windows Defender\", @"\Windows Security\"];

    public static bool IsPartOfWindows(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var p = path.Replace('/', '\\');
        if (Folders.Any(f => p.Contains(f, StringComparison.OrdinalIgnoreCase))) return true;
        const string apps = @"\WindowsApps\";
        var i = p.IndexOf(apps, StringComparison.OrdinalIgnoreCase);
        if (i < 0 || !p[..i].EndsWith("Program Files", StringComparison.OrdinalIgnoreCase)) return false;
        var package = p[(i + apps.Length)..];
        return PackagePrefixes.Any(x => package.StartsWith(x, StringComparison.OrdinalIgnoreCase));
    }
}
