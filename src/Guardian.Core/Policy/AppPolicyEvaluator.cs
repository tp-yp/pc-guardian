using Guardian.Core.Config;

namespace Guardian.Core.Policy;

/// <param name="OriginalName">The original file name written inside the program (version info), if any.</param>
/// <param name="Signer">The publisher in the program's valid digital signature (Authenticode), if signed.</param>
public sealed record ProcessInfo(string Path, string? Sha256, string? OriginalName = null, string? Signer = null);

public sealed record AppDecision(RuleAction Action, string Reason);

/// <summary>Why a program was closed, as shown on the block screen.</summary>
public static class Reasons
{
    public const string BlockRule = "התוכנה נעולה (ברשימת התוכנות החסומות)";
    public const string BlockedHours = "התוכנה חסומה בשעות האלה (שעות חסימה שנקבעו לה)";
    public const string NotAllowed = "מצב \"רק תוכנות מותרות\" פועל, והתוכנה לא ברשימת התוכנות המותרות";
    public const string AdminTool = "חסימת כלי מנהל";
    public const string Video = "חסימת קבצי וידאו";
    public const string OtherAccount = "התוכנה נמצאת בתיקייה של משתמש אחר במחשב";
    public const string Vpn = "חסימת VPN ו-proxy";
}

/// <summary>
/// Decides whether an executable may run now. Block rules win. In block-list mode everything else runs; in
/// allow-list mode only the parts of Windows (found automatically), Geder and allowed programs run.
/// </summary>
public static class AppPolicyEvaluator
{
    public static readonly string[] AlwaysAllowedPrefixes =
    [
        @"C:\Windows\",
    ];

    public static AppDecision Evaluate(AppPolicy policy, ProcessInfo proc, DateTime local, IEnumerable<string>? extraTrustedPrefixes = null)
    {
        if (!policy.Enabled) return new(RuleAction.Allow, "app policy disabled");
        if (ProtectedApps.IsProtected(proc.Path)) return new(RuleAction.Allow, "protected part of Windows");
        var active = policy.Rules.Where(r => r.When.IsActive(local)).ToList();

        var block = active.FirstOrDefault(r => r.Action == RuleAction.Block && Matches(r, proc, forBlock: true));
        if (block != null) return new(RuleAction.Block, block.When.Windows.Count > 0 ? Reasons.BlockedHours : Reasons.BlockRule);

        // Block-list mode: everything else runs.
        if (!policy.DefaultDeny) return new(RuleAction.Allow, "default allow");

        // Allow-list mode: Windows itself (found automatically), Geder, and the allowed programs.
        if (AlwaysAllowedPrefixes.Concat(extraTrustedPrefixes ?? []).Any(p => StartsWith(proc.Path, p)) || SystemApps.IsPartOfWindows(proc.Path))
            return new(RuleAction.Allow, "system path");

        var allow = active.FirstOrDefault(r => r.Action == RuleAction.Allow && Matches(r, proc));
        if (allow != null) return new(RuleAction.Allow, "matched allow rule");

        return new(RuleAction.Block, Reasons.NotAllowed);
    }

    /// <summary>Geder's own folder, which is always allowed.</summary>
    public static List<string> TrustedPrefixes(string selfDir) => [WithSlash(selfDir)];

    private static string WithSlash(string d) => Normalize(d).TrimEnd('\\') + "\\";

    /// <param name="forBlock">Block rules also look at the name written inside the file, so renaming does not help.
    /// Allow rules do not, so a renamed program can never pick up someone else's permission.</param>
    public static bool Matches(AppRule r, ProcessInfo p, bool forBlock = false)
    {
        if (forBlock && p.OriginalName is { Length: > 0 } orig)
        {
            var o = ExeName(orig);
            if (r.OriginalName is { Length: > 0 } ro && string.Equals(ExeName(ro), o, StringComparison.OrdinalIgnoreCase)) return true;
            if (r.FileName is { Length: > 0 } rf && string.Equals(ExeName(rf), o, StringComparison.OrdinalIgnoreCase)) return true;
        }
        // A signed program allowed by place and publisher: any program of that publisher there, also after updates.
        if (r.Signer is { Length: > 0 } sg)
            return r.PathPrefix is { Length: > 0 } sp && PathMatches(p.Path, sp) && string.Equals(sg, p.Signer, StringComparison.Ordinal);
        // A program allowed by place and contents (from a scanned folder) must match both.
        if (r.PathPrefix is { Length: > 0 } pp && r.Sha256 is { Length: > 0 } hh)
            return PathMatches(p.Path, pp) && p.Sha256 is { } ps && string.Equals(hh, ps, StringComparison.OrdinalIgnoreCase);
        if (r.Sha256 is { Length: > 0 } h && p.Sha256 is { } ph && string.Equals(h, ph, StringComparison.OrdinalIgnoreCase)) return true;
        if (r.PathPrefix is { Length: > 0 } pre && PathMatches(p.Path, pre)) return true;
        if (r.FileName is { Length: > 0 } fn && string.Equals(fn, FileNameOf(p.Path), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>A rule for one program (ends with .exe) is that exact file; any other path is a folder.</summary>
    public static bool PathMatches(string path, string rule) =>
        rule.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? string.Equals(Normalize(path), Normalize(rule), StringComparison.OrdinalIgnoreCase)
            : StartsWith(path, rule);

    /// <summary>"Discord" / "discord.exe.mui" / "Discord.exe" all become "Discord.exe".</summary>
    public static string ExeName(string name)
    {
        var n = name.Trim().Trim('"');
        if (n.EndsWith(".mui", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n : n + ".exe";
    }

    private static bool StartsWith(string path, string prefix) =>
        Normalize(path).StartsWith(Normalize(prefix), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string p) => p.Replace('/', '\\');

    private static string FileNameOf(string path)
    {
        var p = Normalize(path);
        var i = p.LastIndexOf('\\');
        return i < 0 ? p : p[(i + 1)..];
    }
}
