using System.Reflection;
using Guardian.Core.Config;
using Guardian.Core.Scheduling;

namespace Guardian.Core.Policy;

/// <summary>
/// A site with time rules (per account, in the "Network" screen). Two parts, each optional, like the Users screen:
/// <list type="bullet">
/// <item><see cref="BlockedDuring"/>: the site is blocked in these hours.</item>
/// <item><see cref="UseMinutes"/> / <see cref="BlockMinutes"/>: from the first visit the site is open for UseMinutes,
/// then blocked for BlockMinutes, and then the next visit starts a new round. "Open 15 minutes, blocked 23:45" = a
/// quarter of an hour a day. The time runs from the first visit, also when the site is closed meanwhile (Geder
/// cannot tell whether a tab is being looked at).</item>
/// </list>
/// </summary>
public sealed class TimedSite
{
    public string Site { get; set; } = "";
    public WeeklySchedule BlockedDuring { get; set; } = new();
    public int UseMinutes { get; set; }
    public int BlockMinutes { get; set; }
}

public enum TimedPhase { Open, Using, Resting, Hours }

public static class TimedSites
{
    public const int MaxMinutes = 7 * 24 * 60;

    /// <summary>Where a site stands now. <paramref name="startUtc"/> is the start of the current round, if one is running.</summary>
    public static (TimedPhase Phase, DateTime? UntilUtc) State(TimedSite t, DateTime? startUtc, DateTime utc, DateTime local)
    {
        if (t.BlockedDuring.Windows.Count > 0 && t.BlockedDuring.IsActive(local)) return (TimedPhase.Hours, null);
        if (t.UseMinutes > 0 && startUtc is { } s)
        {
            var useEnd = s.AddMinutes(t.UseMinutes);
            if (utc < useEnd) return (TimedPhase.Using, useEnd);
            var restEnd = useEnd.AddMinutes(t.BlockMinutes);
            if (utc < restEnd) return (TimedPhase.Resting, restEnd);
        }
        return (TimedPhase.Open, null);
    }

    public static bool IsBlocked(TimedPhase p) => p is TimedPhase.Resting or TimedPhase.Hours;

    /// <summary>A round is over (or never started): the next visit starts a new one.</summary>
    public static bool RoundOver(TimedSite t, DateTime startUtc, DateTime utc) =>
        utc >= startUtc.AddMinutes(t.UseMinutes + Math.Max(0, t.BlockMinutes));

    public static string Key(string? scope, string site) => (scope ?? "").ToLowerInvariant() + "|" + UrlPolicy.NormalizeHost(site);

    /// <summary>
    /// The rules as the filter should see them right now: a blocked timed site is added to the blocked sites
    /// (block list) or taken out of the allowed ones (allow list); an open one is allowed in an allow list.
    /// Returns the same object when nothing changes.
    /// </summary>
    public static NetworkPolicy Apply(NetworkPolicy p, Func<TimedSite, bool> blocked)
    {
        if (p.TimedSites.Count == 0) return p;
        var copy = (NetworkPolicy)Clone.Invoke(p, null)!;
        var shut = p.TimedSites.Where(blocked).Select(t => UrlPolicy.NormalizeHost(t.Site)).Where(h => h.Length > 0).ToList();
        var open = p.TimedSites.Where(t => !blocked(t)).Select(t => UrlPolicy.NormalizeHost(t.Site)).Where(h => h.Length > 0).ToList();
        bool Shut(string entry) => shut.Any(h => UrlPolicy.HostMatches(UrlPolicy.ParseEntry(entry).Host, h));
        if (p.Mode == UrlMode.Blocklist)
        {
            copy.Domains = [.. p.Domains, .. shut];
            copy.Exceptions = p.Exceptions.Where(e => !Shut(e)).ToList();
        }
        else
        {
            copy.Domains = [.. p.Domains.Where(d => !Shut(d)), .. open];
        }
        return copy;
    }

    /// <summary>The whole config with <see cref="Apply"/> done for the general rules and for every account.</summary>
    public static GuardianConfig Effective(GuardianConfig cfg, Func<string?, TimedSite, bool> blocked)
    {
        if (cfg.Network.TimedSites.Count == 0 && cfg.UserPolicies.All(u => u.Network.TimedSites.Count == 0)) return cfg;
        var e = (GuardianConfig)Clone.Invoke(cfg, null)!;
        e.Network = Apply(cfg.Network, t => blocked(null, t));
        e.UserPolicies = cfg.UserPolicies.Select(u => new UserPolicy { UserName = u.UserName, Apps = u.Apps, Network = Apply(u.Network, t => blocked(u.UserName, t)) }).ToList();
        return e;
    }

    /// <summary>Checks and cleans a rule from the management window.</summary>
    public static bool TryValidate(TimedSite t, out string error)
    {
        error = "";
        var e = UrlPolicy.ParseEntry(t.Site ?? "");
        if (e.Host.Length == 0 || e.Host.Length > 253 || e.Host.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || !e.Host.Contains('.'))
        { error = "כתובת האתר לא תקינה."; return false; }
        t.Site = e.Host;
        if (t.UseMinutes is < 0 or > MaxMinutes || t.BlockMinutes is < 0 or > MaxMinutes) { error = "מספר הדקות לא תקין."; return false; }
        if (t.UseMinutes > 0 && t.BlockMinutes == 0) { error = "צריך לקבוע כמה זמן האתר חסום אחרי זמן השימוש."; return false; }
        if (t.UseMinutes == 0) t.BlockMinutes = 0;
        t.BlockedDuring ??= new();
        if (t.BlockedDuring.Windows.Any(w => w.Days.Length == 0)) { error = "Pick at least one day for each time range."; return false; }
        if (t.UseMinutes == 0 && t.BlockedDuring.Windows.Count == 0) { error = "צריך לקבוע שעות חסימה או זמן שימוש."; return false; }
        return true;
    }

    private static readonly MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance)!;
}
