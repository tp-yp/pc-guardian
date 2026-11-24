using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Guardian.Core.Apps;
using Guardian.Core.Config;
using Guardian.Core.Network;
using Guardian.Core.Policy;
using Guardian.Core.Security;
using Guardian.Core.Storage;
using Guardian.Service.Enforcers;

namespace Guardian.Service.Ui;

/// <summary>
/// Local management page on http://127.0.0.1:8898 : live network access log and editing of the
/// URL allow/block lists. Requires the admin or recovery password. Protections: loopback only,
/// Host header check (DNS rebinding), SameSite=Strict HttpOnly session cookie, and a custom
/// request header on every API call (blocks cross-site form posts).
/// </summary>
public sealed class ManagementUi(ILogger<ManagementUi> log, EncryptedConfigStore store, AdminGate gate, AccessLog accessLog,
    AppCatalogScanner catalog, LockoutExemptions exemptions, DirectConnectionWatcher direct, DnsFilterServer dns, IspFilterMonitor isp, TrustedClock clock)
    : BackgroundService
{
    public const int Port = 8898;
    private static readonly string[] AllowedHosts = [$"127.0.0.1:{Port}", $"localhost:{Port}"];
    private readonly ConcurrentDictionary<string, DateTime> _sessions = new();
    private static readonly JsonSerializerOptions Json = EncryptedConfigStore.Json;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var http = new HttpListener();
        http.Prefixes.Add($"http://127.0.0.1:{Port}/");
        http.Prefixes.Add($"http://localhost:{Port}/");
        try { http.Start(); }
        catch (HttpListenerException ex) { log.LogError(ex, "Management UI could not start"); return; }
        using var reg = ct.Register(http.Stop);
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await http.GetContextAsync(); }
            catch (Exception) when (ct.IsCancellationRequested) { break; }
            catch (HttpListenerException) { continue; }
            _ = Task.Run(() => Handle(ctx), ct);
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        try
        {
            res.Headers["X-Frame-Options"] = "DENY";
            res.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'";
            res.Headers["Cache-Control"] = "no-store";
            if (!IPAddress.IsLoopback(req.RemoteEndPoint.Address) || !AllowedHosts.Contains(req.Headers["Host"]?.ToLowerInvariant()))
            { await Send(res, 403, "text/plain", "Forbidden"); return; }

            var path = req.Url!.AbsolutePath;
            if (path == "/" && req.HttpMethod == "GET") { await Send(res, 200, "text/html; charset=utf-8", Page.Html); return; }
            if (!path.StartsWith("/api/") || req.Headers["X-Guardian"] != "1") { await Send(res, 404, "text/plain", "Not found"); return; }

            var body = req.HasEntityBody ? await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(req.InputStream, Json) ?? new() : new();

            if (path == "/api/login" && req.HttpMethod == "POST")
            {
                var auth = gate.Authenticate(store.Load(), body.GetValueOrDefault("password") ?? "");
                if (auth == AuthResult.LockedOut) { await SendJson(res, 429, new { error = "Too many attempts. Try again later." }); return; }
                if (auth == AuthResult.Denied) { await SendJson(res, 401, new { error = "Wrong password." }); return; }
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                _sessions[token] = DateTime.UtcNow.AddMinutes(IdleMinutes());
                res.Headers.Add("Set-Cookie", $"gsid={token}; Path=/; HttpOnly; SameSite=Strict");
                await SendJson(res, 200, new { ok = true });
                return;
            }

            if (!Authorized(req)) { await SendJson(res, 401, new { error = "Login required." }); return; }

            // Which rules a request reads or edits: the general ones (no user) or one account's own rules.
            var who = body.GetValueOrDefault("user") ?? req.QueryString["user"];
            if (string.IsNullOrWhiteSpace(who)) who = null;
            var shownWho = who;
            // The filtered-line settings are for the whole computer, never one account.
            if ((body.GetValueOrDefault("key") ?? body.GetValueOrDefault("list") ?? "").StartsWith("isp") || (body.GetValueOrDefault("key") ?? "").StartsWith("clock.")) who = null;

            switch (path, req.HttpMethod)
            {
                case ("/api/logout", "POST"):
                    _sessions.TryRemove(req.Cookies["gsid"]?.Value ?? "", out _);
                    await SendJson(res, 200, new { ok = true });
                    break;
                case ("/api/log", "GET"):
                    await SendJson(res, 200, accessLog.Recent(300));
                    break;
                case ("/api/network", "GET"):
                    await SendJson(res, 200, store.Load().Network);
                    break;
                case ("/api/catalog", "GET"):
                {
                    var apps = await catalog.GetAsync(refresh: req.QueryString["refresh"] == "1");
                    await SendJson(res, 200, AppCatalog.WithState(apps, UserScopes.View(store.Load(), who)));
                    break;
                }
                case ("/api/catalog/set", "POST"):
                {
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    var exe = body.GetValueOrDefault("path") ?? "";
                    var orig = body.GetValueOrDefault("kind") == "lock" && File.Exists(exe) ? AppBlocker.ReadOriginalName(exe) : null;
                    if (!AppCatalog.TrySet(view, exe, body.GetValueOrDefault("kind") ?? "", body.GetValueOrDefault("value") == "true", out var err, orig))
                    { await SendJson(res, 400, new { error = err }); break; }
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    await SendJson(res, 200, AppCatalog.WithState(await catalog.GetAsync(), UserScopes.View(cfg, who)));
                    break;
                }
                case ("/api/apps/allow", "POST"):
                {
                    var exePath = body.GetValueOrDefault("path") ?? "";
                    var value = body.GetValueOrDefault("scope") == "folder" ? ConfigEditing.AppFolderOf(exePath) : exePath;
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    // Allowing also removes a matching lock, otherwise the lock would still win.
                    view.Apps.Rules.RemoveAll(r => r.Action == RuleAction.Block && r.When.Windows.Count == 0 &&
                        (string.Equals(r.PathPrefix, exePath, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(r.FileName, Path.GetFileName(exePath), StringComparison.OrdinalIgnoreCase)));
                    if (!ConfigEditing.TryEditAppList(view, "allowApps", "add", value, out var err)) { await SendJson(res, 400, new { error = err }); break; }
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    await SendJson(res, 200, new { ok = true, allowed = value, user = UserScopes.Find(cfg, who)?.UserName });
                    break;
                }
                case ("/api/site", "POST"):
                {
                    var target = body.GetValueOrDefault("target") ?? "";
                    if (body.GetValueOrDefault("whole") == "true") target = UrlPolicy.NormalizeHost(target);
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    ConfigEditing.SetSiteAccess(view.Network, target, body.GetValueOrDefault("allow") == "true");
                    if (body.GetValueOrDefault("allow") != "true") view.Network.Enabled = true;
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    await SendJson(res, 200, State(cfg, who));
                    break;
                }
                case ("/api/direct", "GET"):
                {
                    var cfg = store.Load();
                    await SendJson(res, 200, direct.Recent().Select(d => new
                    {
                        d.AppPath, d.LastDestination, d.Count, d.Last,
                        allowed = cfg.Network.AllowDirectApps.Contains(d.AppPath, StringComparer.OrdinalIgnoreCase),
                    }));
                    break;
                }
                case ("/api/users", "GET"):
                {
                    var cfg = store.Load();
                    var accounts = AppCatalogScanner.LocalAccounts();
                    await SendJson(res, 200, new
                    {
                        accounts = accounts.Select(a => new
                        {
                            name = a,
                            rule = cfg.UserLockouts.FirstOrDefault(r => Guardian.Core.Lockout.LockoutPlanner.SameUser(r.UserName, a)),
                            ownRules = UserScopes.Find(cfg, a) != null,
                            unlockedUntil = exemptions.Until.TryGetValue(a, out var u) && u > clock.LocalNow ? u : (DateTime?)null,
                        }),
                        everyoneLocked = ConfigEditing.EveryAccountHasLockout(cfg, accounts),
                    });
                    break;
                }
                case ("/api/users/set", "POST"):
                {
                    var cfg = store.Load();
                    List<Guardian.Core.Scheduling.TimeWindow> windows;
                    try { windows = JsonSerializer.Deserialize<List<Guardian.Core.Scheduling.TimeWindow>>(body.GetValueOrDefault("windows") ?? "[]", Json) ?? []; }
                    catch (JsonException) { await SendJson(res, 400, new { error = "שעות לא תקינות." }); break; }
                    int.TryParse(body.GetValueOrDefault("warning"), out var warn);
                    if (!ConfigEditing.TrySetLockout(cfg, body.GetValueOrDefault("user") ?? "", windows, warn, out var err)) { await SendJson(res, 400, new { error = err }); break; }
                    store.Save(cfg);
                    await SendJson(res, 200, new { ok = true });
                    break;
                }
                case ("/api/users/unlock", "POST"):
                {
                    var user = body.GetValueOrDefault("user") ?? "";
                    if (!int.TryParse(body.GetValueOrDefault("minutes"), out var mins) || mins is < 0 or > 1440 || user.Length == 0)
                    { await SendJson(res, 400, new { error = "Bad request." }); break; }
                    if (mins == 0) exemptions.Until.TryRemove(user, out _); else exemptions.Until[user] = clock.LocalNow.AddMinutes(mins);
                    await SendJson(res, 200, new { ok = true });
                    break;
                }
                case ("/api/users/own", "POST"):
                {
                    var cfg = store.Load();
                    if (!UserScopes.TrySetOwnRules(cfg, body.GetValueOrDefault("name") ?? "", body.GetValueOrDefault("own") == "true", out var err))
                    { await SendJson(res, 400, new { error = err }); break; }
                    store.Save(cfg);
                    await SendJson(res, 200, State(cfg, null));
                    break;
                }
                case ("/api/settings/idle", "POST"):
                {
                    if (!int.TryParse(body.GetValueOrDefault("minutes"), out var m) || !GuardianConfig.IdleLockChoices.Contains(m))
                    { await SendJson(res, 400, new { error = "Bad value." }); break; }
                    var cfg = store.Load();
                    cfg.UiIdleLockMinutes = m;
                    store.Save(cfg);
                    await SendJson(res, 200, State(cfg, null));
                    break;
                }
                case ("/api/health", "GET"):
                    await SendJson(res, 200, new
                    {
                        clockSynced = clock.Synced,
                        clockLastSync = clock.Synced ? clock.LastSync.ToLocalTime() : (DateTime?)null,
                        clockTrusted = clock.LocalNow,
                        dnsLastSuccess = dns.LastUpstreamSuccess == default ? (DateTime?)null : dns.LastUpstreamSuccess.ToLocalTime(),
                        dnsLastError = dns.LastUpstreamError,
                    });
                    break;
                case ("/api/network/msupdates", "POST"):
                {
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    var office = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), @"microsoft shared\ClickToRun\OfficeClickToRun.exe");
                    ConfigEditing.AllowMicrosoftUpdates(view, office);
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    log.LogInformation("UI: Microsoft updates allowed");
                    await SendJson(res, 200, State(cfg, who));
                    break;
                }
                case ("/api/password", "POST"):
                {
                    // Changing a password asks for the current one again, even inside an open session.
                    var cfg = store.Load();
                    var auth = gate.Authenticate(cfg, body.GetValueOrDefault("current") ?? "");
                    if (auth == AuthResult.LockedOut) { await SendJson(res, 429, new { error = "Too many attempts. Try again later." }); break; }
                    if (auth == AuthResult.Denied) { await SendJson(res, 401, new { error = "הסיסמה הנוכחית שגויה." }); break; }
                    if (!ConfigEditing.TrySetPassword(cfg, auth, body.GetValueOrDefault("which") ?? "", body.GetValueOrDefault("next") ?? "", out var err))
                    { await SendJson(res, 400, new { error = err }); break; }
                    store.Save(cfg);
                    log.LogInformation("UI: {Which} password changed", body["which"]);
                    await SendJson(res, 200, new { ok = true });
                    break;
                }
                case ("/api/isp", "GET"):
                    await SendJson(res, 200, IspState(store.Load()));
                    break;
                case ("/api/isp/check", "POST"):
                {
                    var cfg = store.Load();
                    await isp.CheckAsync(cfg.IspFilter);
                    await SendJson(res, 200, IspState(cfg));
                    break;
                }
                case ("/api/isp/trust", "POST"):
                {
                    // "This is my filter": pin the top certificate of the chain seen right now.
                    var cfg = store.Load();
                    var chain = isp.Last.Chain;
                    if (chain.Count == 0) { await SendJson(res, 400, new { error = "עדיין לא נמצאה תעודה. לחצו קודם על \"בדיקה עכשיו\"." }); break; }
                    var thumb = IspFilterCheck.Normalize(chain[^1].Thumbprint);
                    if (!cfg.IspFilter.Thumbprints.Any(t => IspFilterCheck.Normalize(t) == thumb)) cfg.IspFilter.Thumbprints.Add(thumb);
                    store.Save(cfg);
                    log.LogInformation("UI: trusted filter certificate {Thumb}", thumb);
                    await SendJson(res, 200, IspState(cfg));
                    break;
                }
                case ("/api/state", "GET"):
                    await SendJson(res, 200, State(store.Load(), who));
                    break;
                case ("/api/settings", "POST"):
                {
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    if (!ConfigEditing.TrySetToggle(view, body.GetValueOrDefault("key") ?? "", body.GetValueOrDefault("value") == "true", out var err))
                    { await SendJson(res, 400, new { error = err }); break; }
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    log.LogInformation("UI: {Key} = {Value}", body["key"], body["value"]);
                    await SendJson(res, 200, State(cfg, who));
                    break;
                }
                case ("/api/apps/list", "POST"):
                {
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    if (!ConfigEditing.TryEditAppList(view, body.GetValueOrDefault("list") ?? "", body.GetValueOrDefault("action") ?? "",
                            body.GetValueOrDefault("value") ?? "", out var err))
                    { await SendJson(res, 400, new { error = err }); break; }
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    await SendJson(res, 200, State(cfg, who));
                    break;
                }
                case ("/api/network/list", "POST"):
                {
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    if (!ConfigEditing.TryEditNetworkList(view, body.GetValueOrDefault("list") ?? "", body.GetValueOrDefault("action") ?? "",
                            body.GetValueOrDefault("value") ?? "", out var err))
                    { await SendJson(res, 400, new { error = err }); break; }
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    log.LogInformation("UI: {Action} {Value} in {List}", body["action"], body["value"], body["list"]);
                    await SendJson(res, 200, State(cfg, who));
                    break;
                }
                case ("/api/network/mode", "POST"):
                {
                    if (!Enum.TryParse<UrlMode>(body.GetValueOrDefault("mode"), out var mode)) { await SendJson(res, 400, new { error = "Bad mode." }); break; }
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    view.Network.Mode = mode;
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    await SendJson(res, 200, State(cfg, who));
                    break;
                }
                default:
                    await SendJson(res, 404, new { error = "Not found." });
                    break;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "UI request failed");
            try { await SendJson(res, 500, new { error = "Internal error." }); } catch { /* client gone */ }
        }
    }

    private object IspState(GuardianConfig cfg)
    {
        var r = isp.Last;
        // Shown against the saved settings, so a just-trusted certificate counts at once.
        var status = r.Chain.Count > 0 && r.Status != IspFilterStatus.NoNetwork
            ? (IspFilterCheck.IsFilterChain(cfg.IspFilter, r.Chain) ? IspFilterStatus.Filtered : IspFilterStatus.NotFiltered)
            : r.Status;
        return new
        {
            required = cfg.IspFilter.Required,
            status = status.ToString(),
            blocked = cfg.IspFilter.Required && status != IspFilterStatus.Filtered,
            time = r.Status == IspFilterStatus.Unknown ? (DateTime?)null : r.Time,
            detail = r.Detail,
            signer = IspFilterCheck.Signer(r.Chain),
            chain = r.Chain.Select(c => new { name = IspFilterCheck.ShortName(c.Subject), issuer = IspFilterCheck.ShortName(c.Issuer), thumbprint = c.Thumbprint }),
            names = cfg.IspFilter.IssuerNames,
            providers = cfg.IspFilter.Providers,
            thumbprints = cfg.IspFilter.Thumbprints,
            apps = cfg.IspFilter.ClientApps,
        };
    }

    private static object State(GuardianConfig cfg, string? who)
    {
        var c = UserScopes.View(cfg, who);
        return StateOf(c, UserScopes.Find(cfg, who)?.UserName, cfg.UserPolicies.Select(p => p.UserName).ToList());
    }

    private static object StateOf(GuardianConfig c, string? scope, List<string> scopes) => new
    {
        scope,
        scopes,
        settings = new Dictionary<string, bool>
        {
            ["apps.enabled"] = c.Apps.Enabled,
            ["apps.defaultDeny"] = c.Apps.DefaultDeny,
            ["apps.trustProgramFiles"] = c.Apps.TrustProgramFiles,
            ["network.enabled"] = c.Network.Enabled,
            ["network.urlFiltering"] = c.Network.UrlFiltering,
            ["boot.lockdown"] = c.Boot.Lockdown,
            ["isp.required"] = c.IspFilter.Required,
            ["isp.netfree"] = c.IspFilter.Providers.Contains("NetFree", StringComparer.OrdinalIgnoreCase),
            ["isp.etrog"] = c.IspFilter.Providers.Contains("Etrog", StringComparer.OrdinalIgnoreCase),
            ["clock.locked"] = c.Clock.Locked,
        },
        mode = c.Network.Mode.ToString(),
        lists = new Dictionary<string, List<string>>
        {
            ["domains"] = c.Network.Domains,
            ["noDecrypt"] = c.Network.NoDecryptDomains,
            ["exceptions"] = c.Network.Exceptions,
            ["blockedApps"] = c.Network.BlockedAppPaths,
            ["directApps"] = c.Network.AllowDirectApps,
            ["allowApps"] = ConfigEditing.SimpleAppList(c, RuleAction.Allow),
            ["blockApps"] = ConfigEditing.SimpleAppList(c, RuleAction.Block),
            // Provider names left by an older version are chosen with the provider boxes now, so they are not listed.
            ["ispNames"] = c.IspFilter.IssuerNames.Where(n => !IspFilterCheck.ProviderNames.Values.Any(v => v.Contains(n, StringComparer.OrdinalIgnoreCase))).ToList(),
            ["ispThumbprints"] = c.IspFilter.Thumbprints,
            ["ispApps"] = c.IspFilter.ClientApps,
        },
        lockouts = c.UserLockouts.Count,
        idleMinutes = c.UiIdleLockMinutes > 0 ? c.UiIdleLockMinutes : 5,
        idleChoices = GuardianConfig.IdleLockChoices,
    };

    private bool Authorized(HttpListenerRequest req)
    {
        var token = req.Cookies["gsid"]?.Value;
        if (token is null || !_sessions.TryGetValue(token, out var exp) || exp < DateTime.UtcNow) return false;
        // Only real use keeps the window unlocked; background refreshes (the activity list) do not.
        if (req.Headers["X-Active"] == "1") _sessions[token] = DateTime.UtcNow.AddMinutes(IdleMinutes());
        return true;
    }

    private int IdleMinutes()
    {
        try { var m = store.Load().UiIdleLockMinutes; return m > 0 ? m : 5; } catch { return 5; }
    }

    private static Task SendJson(HttpListenerResponse res, int status, object value) =>
        Send(res, status, "application/json", JsonSerializer.Serialize(value, Json));

    private static async Task Send(HttpListenerResponse res, int status, string type, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        res.StatusCode = status;
        res.ContentType = type;
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }
}
