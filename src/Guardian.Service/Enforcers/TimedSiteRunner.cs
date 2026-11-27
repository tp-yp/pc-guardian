using System.Text.Json;
using Guardian.Core.Config;
using Guardian.Core.Policy;

namespace Guardian.Service.Enforcers;

/// <summary>
/// Runs the clocks of sites with time rules (<see cref="TimedSites"/>): the first allowed visit starts a round,
/// and the filter gets rules in which the sites that are blocked right now are blocked. The rounds are kept in
/// GuardianData, so restarting the computer does not start a site's time again. Times come from the trusted clock.
/// </summary>
public sealed class TimedSiteRunner
{
    private static string StatePath => Path.Combine(Paths.DataDir, "timed-sites.json");
    private readonly ILogger<TimedSiteRunner> _log;
    private readonly TrustedClock _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTime> _start;
    private volatile GuardianConfig? _cfg;
    private GuardianConfig? _lastCfg;
    private string _sig = "";

    public TimedSiteRunner(ILogger<TimedSiteRunner> log, AccessLog access, TrustedClock clock)
    {
        _log = log;
        _clock = clock;
        _start = Load();
        access.Added += OnAccess;
    }

    /// <summary>The rules for the filter when they changed since the last call (new settings, or a site opened or closed); otherwise null.</summary>
    public GuardianConfig? Tick(GuardianConfig cfg)
    {
        _cfg = cfg;
        var utc = _clock.UtcNow;
        var local = _clock.LocalNow;
        lock (_gate)
        {
            // Finished rounds, and rounds of rules that are gone, are forgotten.
            var rules = AllRules(cfg).ToDictionary(r => TimedSites.Key(r.Scope, r.Rule.Site), r => r.Rule);
            var gone = _start.Where(kv => !rules.TryGetValue(kv.Key, out var t) || t.UseMinutes == 0 || TimedSites.RoundOver(t, kv.Value, utc)).Select(kv => kv.Key).ToList();
            foreach (var k in gone) _start.Remove(k);
            if (gone.Count > 0) Save();
        }
        var sig = string.Join(";", AllRules(cfg).Select(r => TimedSites.Key(r.Scope, r.Rule.Site) + "=" + Blocked(r.Scope, r.Rule, utc, local)));
        if (ReferenceEquals(cfg, _lastCfg) && sig == _sig) return null;
        if (_sig.Length > 0 && sig != _sig) _log.LogInformation("Timed sites now: {State}", sig);
        _lastCfg = cfg;
        _sig = sig;
        return TimedSites.Effective(cfg, (scope, t) => Blocked(scope, t, utc, local));
    }

    /// <summary>Each timed site of an account (null = the general rules) with where it stands now, for the management window.</summary>
    public IEnumerable<object> Status(GuardianConfig cfg, string? scope)
    {
        var rules = scope is null ? cfg.Network.TimedSites : UserScopes.Find(cfg, scope)?.Network.TimedSites ?? cfg.Network.TimedSites;
        var name = scope is null ? null : UserScopes.Find(cfg, scope)?.UserName;
        foreach (var t in rules)
        {
            var (phase, until) = TimedSites.State(t, StartOf(name, t), _clock.UtcNow, _clock.LocalNow);
            yield return new { site = t.Site, phase = phase.ToString(), until = until?.ToLocalTime() };
        }
    }

    private bool Blocked(string? scope, TimedSite t, DateTime utc, DateTime local) =>
        TimedSites.IsBlocked(TimedSites.State(t, StartOf(scope, t), utc, local).Phase);

    private DateTime? StartOf(string? scope, TimedSite t)
    {
        lock (_gate) return _start.TryGetValue(TimedSites.Key(scope, t.Site), out var s) ? s : null;
    }

    private static IEnumerable<(string? Scope, TimedSite Rule)> AllRules(GuardianConfig cfg) =>
        cfg.Network.TimedSites.Select(t => ((string?)null, t))
            .Concat(cfg.UserPolicies.SelectMany(u => u.Network.TimedSites.Select(t => ((string?)u.UserName, t))));

    /// <summary>
    /// An allowed visit starts the round of a site with a time limit. With the full-URL filter on, only real web
    /// requests count; without it, name lookups do (a browser may also look a site up ahead of time).
    /// When the account behind a lookup is not known, the visit counts for every account that has the site.
    /// </summary>
    private void OnAccess(AccessEntry e)
    {
        var cfg = _cfg;
        if (cfg is null || !e.Allowed) return;
        if (!(e.Source == "web" || (e.Source == "dns" && !cfg.Network.UrlFiltering))) return;
        var host = UrlPolicy.NormalizeHost(e.Target);
        if (host.Length == 0) return;
        var scopes = e.User != null ? [UserScopes.Find(cfg, e.User)?.UserName] : new List<string?> { null }.Concat(cfg.UserPolicies.Select(u => (string?)u.UserName)).ToList();
        var utc = _clock.UtcNow;
        var local = _clock.LocalNow;
        var changed = false;
        lock (_gate)
        {
            foreach (var scope in scopes)
            {
                var rules = scope is null ? cfg.Network.TimedSites : UserScopes.Find(cfg, scope)?.Network.TimedSites ?? [];
                foreach (var t in rules.Where(t => t.UseMinutes > 0 && UrlPolicy.HostMatches(host, t.Site)))
                {
                    var key = TimedSites.Key(scope, t.Site);
                    var start = _start.TryGetValue(key, out var s) ? s : (DateTime?)null;
                    if (TimedSites.State(t, start, utc, local).Phase != TimedPhase.Open) continue;
                    _start[key] = utc;
                    changed = true;
                    _log.LogInformation("Timed site {Site} opened for {Scope}: {Min} minutes", t.Site, scope ?? "(general)", t.UseMinutes);
                }
            }
            if (changed) Save();
        }
    }

    private static Dictionary<string, DateTime> Load()
    {
        try { return File.Exists(StatePath) ? JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(StatePath)) ?? [] : []; }
        catch (Exception) { return []; }
    }

    private void Save()
    {
        try { File.WriteAllText(StatePath, JsonSerializer.Serialize(_start)); } catch (Exception) { /* best effort */ }
    }
}
