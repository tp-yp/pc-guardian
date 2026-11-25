namespace Guardian.Core.Policy;

/// <summary>
/// "Block video files" (per account): video players are closed, and so is any program opened with a
/// video file (double-click, "Open with", a file:/// link in a browser).
/// </summary>
public static class VideoFiles
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".f4v", ".mpg", ".mpeg", ".mpe", ".m2v",
        ".m2ts", ".3gp", ".3g2", ".ogv", ".vob", ".divx", ".xvid", ".asf", ".rm", ".rmvb", ".dv", ".mxf",
    };

    /// <summary>Programs made for playing video (also when renamed: matched by the name inside the file).</summary>
    public static readonly HashSet<string> Players = new(StringComparer.OrdinalIgnoreCase)
    {
        "video.ui.exe", "microsoft.media.player.exe", "wmplayer.exe", "vlc.exe", "mpc-hc.exe", "mpc-hc64.exe",
        "mpc-be.exe", "mpc-be64.exe", "potplayer.exe", "potplayer64.exe", "potplayermini.exe", "potplayermini64.exe",
        "kmplayer.exe", "kmplayer64x.exe", "gom.exe", "gomplayer.exe", "mpv.exe", "smplayer.exe", "kodi.exe",
        "mplayer.exe", "bsplayer.exe", "realplay.exe", "quicktimeplayer.exe", "5kplayer.exe", "daumpotplayer.exe",
    };

    public static bool IsPlayer(string path, string? originalName)
    {
        var file = path.Replace('/', '\\').Split('\\')[^1];
        return Players.Contains(file) || (originalName is { Length: > 0 } o && Players.Contains(AppPolicyEvaluator.ExeName(o)));
    }

    /// <summary>True if a program's command line names a video file.</summary>
    public static bool InCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        var s = Uri.UnescapeDataString(commandLine.Replace('+', ' '));
        foreach (var ext in Extensions)
        {
            var i = 0;
            while ((i = s.IndexOf(ext, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = i + ext.Length;
                // The extension must end a word: followed by nothing, a quote, a space or a URL part.
                if (end == s.Length || s[end] is '"' or '\'' or ' ' or '?' or '#' or '&' or ',') return true;
                i = end;
            }
        }
        return false;
    }
}
