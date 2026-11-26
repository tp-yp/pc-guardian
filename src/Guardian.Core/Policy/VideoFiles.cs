namespace Guardian.Core.Policy;

/// <summary>
/// "Block video files" (per account): any program opened with a video file (double-click, "Open with",
/// a file:/// link in a browser) is closed. Players opened without a video file stay open.
/// </summary>
public static class VideoFiles
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".f4v", ".mpg", ".mpeg", ".mpe", ".m2v",
        ".m2ts", ".3gp", ".3g2", ".ogv", ".vob", ".divx", ".xvid", ".asf", ".rm", ".rmvb", ".dv", ".mxf",
    };

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
