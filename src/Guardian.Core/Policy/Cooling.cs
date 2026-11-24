using System.Text.Json;
using Guardian.Core.Apps;
using Guardian.Core.Config;
using Guardian.Core.Lockout;
using Guardian.Core.Scheduling;
using Guardian.Core.Storage;

namespace Guardian.Core.Policy;

/// <summary>Decides which management-window requests weaken protection (and so wait for the delay).</summary>
public static class Cooling
{
    /// <summary>Switches that weaken protection when turned on (all others weaken when turned off).</summary>
    private static readonly HashSet<string> WeakWhenOn = ["apps.trustProgramFiles", "isp.netfree", "isp.etrog"];

    public static bool IsActive(GuardianConfig cfg) => cfg.Cooling.Hours > 0;

    public static bool IsWeakening(GuardianConfig cfg, string? who, string path, IReadOnlyDictionary<string, string> body)
    {
        string B(string k) => body.TryGetValue(k, out var v) ? v ?? "" : "";
        var on = B("value") == "true";
        var view = UserScopes.View(Clone(cfg), who);
        switch (path)
        {
            case "/api/settings":
                return WeakWhenOn.Contains(B("key")) ? on : !on;
            case "/api/cooling/set":
                return !int.TryParse(B("hours"), out var h) || h < cfg.Cooling.Hours;
            case "/api/catalog/set":
                return B("kind") == "direct" ? on : !on;
            case "/api/catalog/hours":
                return !Covers(Windows(B("windows")), AppCatalog.HoursOf(view, B("path")));
            case "/api/apps/list":
                return (B("list"), B("action")) is ("blockApps", "remove") or ("allowApps", "add");
            case "/api/apps/allow":
            case "/api/network/msupdates":
            case "/api/isp/trust":
                return true;
            case "/api/site":
                return B("allow") == "true";
            case "/api/network/list":
            {
                var add = B("action") == "add";
                return B("list") switch
                {
                    "domains" => view.Network.Mode == UrlMode.Allowlist ? add : !add,
                    "blockedApps" => !add,
                    _ => add, // exceptions, noDecrypt, directApps, filter names / certificates / apps
                };
            }
            case "/api/network/mode":
                return B("mode") == nameof(UrlMode.Blocklist) && view.Network.Mode == UrlMode.Allowlist;
            case "/api/users/set":
            {
                var old = cfg.UserLockouts.FirstOrDefault(r => LockoutPlanner.SameUser(r.UserName, B("user")));
                if (old is null) return false;
                int.TryParse(B("breakEvery"), out var brk);
                var breakWeaker = old.BreakEveryMinutes > 0 && (brk == 0 || brk > old.BreakEveryMinutes);
                return breakWeaker || !Covers(Windows(B("windows")), old.LockedDuring.Windows);
            }
            case "/api/users/unlock":
            case "/api/tools/unlock":
                return int.TryParse(B("minutes"), out var m) && m > 0;
            case "/api/users/own":
                return B("own") != "true";
            case "/api/wifi/list":
                return B("action") == "add";
            default:
                return false;
        }
    }

    private static List<TimeWindow> Windows(string json)
    {
        try { return JsonSerializer.Deserialize<List<TimeWindow>>(json.Length == 0 ? "[]" : json, EncryptedConfigStore.Json) ?? []; }
        catch (JsonException) { return []; }
    }

    /// <summary>True if every old time range is still there (adding ranges makes things stricter).</summary>
    private static bool Covers(List<TimeWindow> now, List<TimeWindow> old)
    {
        static string Key(TimeWindow w) => string.Join(",", w.Days.OrderBy(d => d)) + "|" + w.Start + "|" + w.End;
        var set = now.Select(Key).ToHashSet();
        return old.All(o => set.Contains(Key(o)));
    }

    private static GuardianConfig Clone(GuardianConfig c) =>
        JsonSerializer.Deserialize<GuardianConfig>(JsonSerializer.Serialize(c, EncryptedConfigStore.Json), EncryptedConfigStore.Json)!;
}
