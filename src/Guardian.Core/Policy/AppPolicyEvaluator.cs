using Guardian.Core.Config;

namespace Guardian.Core.Policy;

public sealed record ProcessInfo(string Path, string? Sha256);

public sealed record AppDecision(RuleAction Action, string Reason);

/// <summary>
/// Decides whether an executable may run now. Order: Block rules win over Allow rules;
/// if nothing matches, DefaultDeny decides. Windows system folders are always allowed
/// so the OS itself keeps working.
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
        var active = policy.Rules.Where(r => r.When.IsActive(local)).ToList();

        var block = active.FirstOrDefault(r => r.Action == RuleAction.Block && Matches(r, proc));
        if (block != null) return new(RuleAction.Block, "matched block rule");

        if (AlwaysAllowedPrefixes.Concat(extraTrustedPrefixes ?? []).Any(p => StartsWith(proc.Path, p)))
            return new(RuleAction.Allow, "system path");

        var allow = active.FirstOrDefault(r => r.Action == RuleAction.Allow && Matches(r, proc));
        if (allow != null) return new(RuleAction.Allow, "matched allow rule");

        return policy.DefaultDeny ? new(RuleAction.Block, "unknown executable (default deny)")
                                  : new(RuleAction.Allow, "default allow");
    }

    public static bool Matches(AppRule r, ProcessInfo p)
    {
        if (r.Sha256 is { Length: > 0 } h && p.Sha256 is { } ph && string.Equals(h, ph, StringComparison.OrdinalIgnoreCase)) return true;
        if (r.PathPrefix is { Length: > 0 } pre && StartsWith(p.Path, pre)) return true;
        if (r.FileName is { Length: > 0 } fn && string.Equals(fn, FileNameOf(p.Path), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
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
