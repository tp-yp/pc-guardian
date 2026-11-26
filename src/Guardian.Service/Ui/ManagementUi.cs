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
    AppCatalogScanner catalog, LockoutExemptions exemptions, AdminToolsExemptions toolsExempt, DirectConnectionWatcher direct, DnsFilterServer dns, IspFilterMonitor isp, TrustedClock clock, BootGuard boot)
    : BackgroundService
{
    public const int Port = 8898;
    private static readonly string[] AllowedHosts = [$"127.0.0.1:{Port}", $"localhost:{Port}"];
    private readonly ConcurrentDictionary<string, DateTime> _sessions = new();
    private static readonly JsonSerializerOptions Json = EncryptedConfigStore.Json;
    // Delayed changes are carried out by sending the saved request to this window again, marked with this secret.
    private readonly string _replaySecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _ = Task.Run(() => ReplayLoop(ct), ct);
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
            if ((body.GetValueOrDefault("key") ?? body.GetValueOrDefault("list") ?? "").StartsWith("isp") || (body.GetValueOrDefault("key") ?? "").StartsWith("clock.") || (body.GetValueOrDefault("key") ?? "").StartsWith("vpn.") || (body.GetValueOrDefault("key") ?? "").StartsWith("wifi.") || (body.GetValueOrDefault("key") ?? "").StartsWith("browser.")) who = null;

            // "Delay before turning off": a change that weakens protection is kept and carried out later.
            if (req.HttpMethod == "POST" && !IsReplay(req))
            {
                var cur = store.Load();
                if (Cooling.IsActive(cur) && Cooling.IsWeakening(cur, who, path, body))
                {
                    var pending = new PendingChange
                    {
                        Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)), Path = path, Body = body, User = who,
                        RequestedUtc = clock.UtcNow, DueUtc = clock.UtcNow.AddMinutes(cur.Cooling.Minutes),
                    };
                    cur.Cooling.Pending.Add(pending);
                    store.Save(cur);
                    log.LogInformation("UI: {Path} delayed until {Due:u}", path, pending.DueUtc);
                    await SendJson(res, 202, new { pending = true, due = pending.DueUtc.ToLocalTime(), minutes = cur.Cooling.Minutes, delay = CoolingPolicy.Describe(cur.Cooling.Minutes) });
                    return;
                }
            }

            switch (path, req.HttpMethod)
            {
                case ("/api/cooling", "GET"):
                {
                    var cfg = store.Load();
                    await SendJson(res, 200, new
                    {
                        minutes = cfg.Cooling.Minutes,
                        pending = cfg.Cooling.Pending.Select(p => new { p.Id, p.Path, p.Body, p.User, due = p.DueUtc.ToLocalTime() }),
                        uninstallDue = cfg.Cooling.UninstallDueUtc?.ToLocalTime(),
                        uninstallReady = cfg.Cooling.UninstallDueUtc is { } u && u <= clock.UtcNow,
                    });
                    break;
                }
                case ("/api/cooling/set", "POST"):
                {
                    if (!Cooling.TryMinutes(body, out var minutes))
                    { await SendJson(res, 400, new { error = "מספר דקות לא תקין." }); break; }
                    var cfg = store.Load();
                    cfg.Cooling.Minutes = minutes;
                    if (minutes == 0) cfg.Cooling.UninstallDueUtc = null;
                    store.Save(cfg);
                    log.LogInformation("UI: delay before turning off = {M} min", minutes);
                    await SendJson(res, 200, new { ok = true });
                    break;
                }
                case ("/api/cooling/cancel", "POST"):
                {
                    var cfg = store.Load();
                    cfg.Cooling.Pending.RemoveAll(p => p.Id == body.GetValueOrDefault("id"));
                    store.Save(cfg);
                    await SendJson(res, 200, new { ok = true });
                    break;
                }
                case ("/api/cooling/uninstall", "POST"):
                {
                    var cfg = store.Load();
                    if (body.GetValueOrDefault("action") == "cancel") cfg.Cooling.UninstallDueUtc = null;
                    else cfg.Cooling.UninstallDueUtc ??= clock.UtcNow.AddMinutes(cfg.Cooling.Minutes);
                    store.Save(cfg);
                    log.LogInformation("UI: removal {Action}", body.GetValueOrDefault("action") ?? "request");
                    await SendJson(res, 200, new { ok = true, due = cfg.Cooling.UninstallDueUtc?.ToLocalTime() });
                    break;
                }
                case ("/api/logout", "POST"):
                    _sessions.TryRemove(req.Cookies["gsid"]?.Value ?? "", out _);
                    await SendJson(res, 200, new { ok = true });
                    break;
                case ("/api/log", "GET"):
                {
                    double.TryParse(req.QueryString["hours"], System.Globalization.CultureInfo.InvariantCulture, out var hours);
                    accessLog.KeepHours = store.Load().ActivityHours;
                    await SendJson(res, 200, accessLog.Recent(3000, hours is > 0 and <= 24 ? hours : 1));
                    break;
                }
                case ("/api/settings/activity", "POST"):
                {
                    if (!int.TryParse(body.GetValueOrDefault("hours"), out var h) || !GuardianConfig.ActivityHourChoices.Contains(h))
                    { await SendJson(res, 400, new { error = "Bad request." }); break; }
                    var cfg = store.Load();
                    cfg.ActivityHours = h;
                    store.Save(cfg);
                    accessLog.KeepHours = h;
                    await SendJson(res, 200, new { ok = true });
                    break;
                }
                case ("/api/network", "GET"):
                    await SendJson(res, 200, store.Load().Network);
                    break;
                case ("/api/catalog", "GET"):
                {
                    var apps = await catalog.GetAsync(refresh: req.QueryString["refresh"] == "1");
                    await SendJson(res, 200, CatalogFor(apps, store.Load(), who));
                    break;
                }
                case ("/api/catalog/set", "POST"):
                {
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    var exe = body.GetValueOrDefault("path") ?? "";
                    var orig = body.GetValueOrDefault("kind") == "lock" && File.Exists(exe) ? AppBlocker.ReadOriginalName(exe) : null;
                    if (body.GetValueOrDefault("kind") == "allow")
                    {
                        // Allow-list mode: allow or un-allow this one program.
                        if (body.GetValueOrDefault("value") == "true")
                        {
                            view.Apps.Rules.RemoveAll(r => r.Action == RuleAction.Block && r.When.Windows.Count == 0 && string.Equals(r.PathPrefix, exe, StringComparison.OrdinalIgnoreCase));
                            if (!AllowOne(view, exe, out var e2)) { await SendJson(res, 400, new { error = e2 }); break; }
                        }
                        else if (view.Apps.Rules.RemoveAll(r => r.Action == RuleAction.Allow && string.Equals(r.PathPrefix, exe, StringComparison.OrdinalIgnoreCase)) == 0)
                        { await SendJson(res, 400, new { error = "התוכנה מותרת דרך תיקייה או שם קובץ ברשימה הידנית. אפשר להסיר אותה שם." }); break; }
                        UserScopes.Commit(cfg, view);
                        store.Save(cfg);
                        await SendJson(res, 200, CatalogFor(await catalog.GetAsync(), cfg, who));
                        break;
                    }
                    if (!AppCatalog.TrySet(view, exe, body.GetValueOrDefault("kind") ?? "", body.GetValueOrDefault("value") == "true", out var err, orig))
                    { await SendJson(res, 400, new { error = err }); break; }
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    await SendJson(res, 200, CatalogFor(await catalog.GetAsync(), cfg, who));
                    break;
                }
                case ("/api/catalog/hours", "POST"):
                {
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    var exe = body.GetValueOrDefault("path") ?? "";
                    List<Guardian.Core.Scheduling.TimeWindow> windows;
                    try { windows = JsonSerializer.Deserialize<List<Guardian.Core.Scheduling.TimeWindow>>(body.GetValueOrDefault("windows") ?? "[]", Json) ?? []; }
                    catch (JsonException) { await SendJson(res, 400, new { error = "שעות לא תקינות." }); break; }
                    var orig = windows.Count > 0 && File.Exists(exe) ? AppBlocker.ReadOriginalName(exe) : null;
                    if (!AppCatalog.TrySetHours(view, exe, windows, out var err, orig)) { await SendJson(res, 400, new { error = err }); break; }
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    log.LogInformation("UI: blocked hours for {Exe}: {N} ranges", exe, windows.Count);
                    await SendJson(res, 200, CatalogFor(await catalog.GetAsync(), cfg, who));
                    break;
                }
                case ("/api/catalog/sites", "POST"):
                {
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    List<string> sites;
                    try { sites = JsonSerializer.Deserialize<List<string>>(body.GetValueOrDefault("sites") ?? "[]") ?? []; }
                    catch (JsonException) { await SendJson(res, 400, new { error = "Invalid value." }); break; }
                    if (!AppCatalog.TrySetSites(view, body.GetValueOrDefault("path") ?? "", sites, out var err)) { await SendJson(res, 400, new { error = err }); break; }
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    log.LogInformation("UI: site list for {Exe}: {N} sites", body.GetValueOrDefault("path"), sites.Count);
                    await SendJson(res, 200, CatalogFor(await catalog.GetAsync(), cfg, who));
                    break;
                }
                case ("/api/tools/unlock", "POST"):
                {
                    // Temporary release of the admin tools for the account being managed (or everyone in the general scope).
                    if (!int.TryParse(body.GetValueOrDefault("minutes"), out var mins) || mins is < 0 or > 480)
                    { await SendJson(res, 400, new { error = "Bad request." }); break; }
                    var key = who ?? "*";
                    if (mins == 0) toolsExempt.Until.TryRemove(key, out _); else toolsExempt.Until[key] = clock.LocalNow.AddMinutes(mins);
                    log.LogInformation("UI: admin tools released for {Who} for {Min} min", key, mins);
                    await SendJson(res, 200, new { ok = true, until = mins == 0 ? (DateTime?)null : toolsExempt.Until[key] });
                    break;
                }
                case ("/api/wifi", "GET"):
                {
                    var cfg = store.Load();
                    var saved = WifiGuard.SavedNetworks();
                    await SendJson(res, 200, new { on = cfg.Wifi.KnownOnly, allowed = cfg.Wifi.Allowed, saved, hasWifi = saved != null });
                    break;
                }
                case ("/api/wifi/list", "POST"):
                {
                    var cfg = store.Load();
                    if (!ConfigEditing.TryEditWifiList(cfg, body.GetValueOrDefault("action") ?? "", body.GetValueOrDefault("value") ?? "", out var err))
                    { await SendJson(res, 400, new { error = err }); break; }
                    store.Save(cfg);
                    await SendJson(res, 200, new { ok = true });
                    break;
                }
                case ("/api/apps/allow", "POST"):
                {
                    var exePath = body.GetValueOrDefault("path") ?? "";
                    var folder = body.GetValueOrDefault("scope") == "folder";
                    var value = folder ? ConfigEditing.AppFolderOf(exePath) : exePath;
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    // Allowing also removes a matching lock, otherwise the lock would still win.
                    view.Apps.Rules.RemoveAll(r => r.Action == RuleAction.Block && r.When.Windows.Count == 0 &&
                        (string.Equals(r.PathPrefix, exePath, StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(r.FileName, Path.GetFileName(exePath), StringComparison.OrdinalIgnoreCase)));
                    if (folder && Directory.Exists(value)) ConfigEditing.AllowScanned(view, value, FolderScan.Scan(value));
                    else if (!AllowOne(view, exePath, out var err)) { await SendJson(res, 400, new { error = err }); break; }
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    await SendJson(res, 200, new { ok = true, allowed = value, user = UserScopes.Find(cfg, who)?.UserName });
                    break;
                }
                case ("/api/apps/allowRunning", "POST"):
                {
                    // Allow-list mode helper: every program open right now (outside Windows) becomes allowed.
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    var n = 0;
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in System.Diagnostics.Process.GetProcesses())
                    {
                        using (p)
                        {
                            if (p.SessionId == 0) continue;
                            var exeRun = Guardian.Service.Native.ProcessPath.Get(p.Id);
                            if (string.IsNullOrEmpty(exeRun) || !seen.Add(exeRun)) continue;
                            var owner = Guardian.Service.Native.ProcessUser.Of(p.Id) ?? "";
                            if (who != null && !string.Equals(owner, who, StringComparison.OrdinalIgnoreCase) && !owner.EndsWith("\\" + who, StringComparison.OrdinalIgnoreCase)) continue;
                            if (ProtectedApps.IsProtected(exeRun) || SystemApps.IsPartOfWindows(exeRun) || exeRun.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                            if (AppCatalog.IsAllowed(view, exeRun)) continue;
                            if (AllowOne(view, exeRun, out _)) n++;
                        }
                    }
                    UserScopes.Commit(cfg, view);
                    store.Save(cfg);
                    log.LogInformation("UI: {N} running programs allowed", n);
                    await SendJson(res, 200, new { count = n });
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
                    int.TryParse(body.GetValueOrDefault("breakEvery"), out var breakEvery);
                    List<Guardian.Core.Scheduling.TimeWindow> breakWindows;
                    try { breakWindows = JsonSerializer.Deserialize<List<Guardian.Core.Scheduling.TimeWindow>>(body.GetValueOrDefault("breakWindows") ?? "[]", Json) ?? []; }
                    catch (JsonException) { await SendJson(res, 400, new { error = "שעות לא תקינות." }); break; }
                    if (!ConfigEditing.TrySetLockout(cfg, body.GetValueOrDefault("user") ?? "", windows, warn, out var err, breakEvery, breakWindows)) { await SendJson(res, 400, new { error = err }); break; }
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
                case ("/api/dashboard", "GET"):
                {
                    var cfg = store.Load();
                    var accounts = AppCatalogScanner.LocalAccounts();
                    List<string> online;
                    try { online = Native.Wts.UserSessions().Select(x => x.UserName).ToList(); } catch (Exception) { online = []; }
                    var now = clock.LocalNow;
                    var ispNow = IspState(cfg);
                    await SendJson(res, 200, new
                    {
                        computer = new
                        {
                            network = cfg.Network.Enabled, urlFiltering = cfg.Network.UrlFiltering, boot = cfg.Boot.Lockdown,
                            clock = cfg.Clock.Locked, vpn = cfg.Vpn.Block, wifi = cfg.Wifi.KnownOnly, isp = ispNow,
                            otherFirewalls = SecurityProducts.ActiveThirdPartyFirewalls(),
                        },
                        general = UserScopes.Summarize(cfg, null),
                        users = accounts.Select(a =>
                        {
                            var rule = cfg.UserLockouts.FirstOrDefault(r => Guardian.Core.Lockout.LockoutPlanner.SameUser(r.UserName, a));
                            var until = exemptions.Until.TryGetValue(a, out var u) && u > now ? u : (DateTime?)null;
                            return new
                            {
                                name = a,
                                own = UserScopes.Find(cfg, a) != null,
                                online = online.Any(o => Guardian.Core.Lockout.LockoutPlanner.SameUser(o, a)),
                                lockedNow = until == null && rule != null && rule.LockedDuring.Windows.Count > 0 && rule.LockedDuring.Windows.Any(w => w.Contains(now)),
                                unlockedUntil = until,
                                summary = UserScopes.Summarize(cfg, a),
                            };
                        }),
                    });
                    break;
                }
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
                case ("/api/boot", "GET"):
                    await SendJson(res, 200, boot.Status);
                    break;
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
                case ("/api/release", "POST"):
                {
                    // "Release all restrictions" for everyone, or bring the settings from before back.
                    var cfg = store.Load();
                    if (body.GetValueOrDefault("action") == "undo")
                    {
                        if (!ConfigEditing.TryUndoRelease(cfg)) { await SendJson(res, 400, new { error = "אין הגבלות שמורות להחזיר." }); break; }
                        log.LogInformation("UI: restrictions brought back");
                    }
                    else
                    {
                        ConfigEditing.ReleaseAll(cfg);
                        log.LogInformation("UI: all restrictions released");
                    }
                    store.Save(cfg);
                    await SendJson(res, 200, State(cfg, who));
                    break;
                }
                case ("/api/backup", "GET"):
                    await SendJson(res, 200, new { file = ConfigEditing.Export(store.Load()) });
                    break;
                case ("/api/backup/restore", "POST"):
                {
                    var cfg = store.Load();
                    try { ConfigEditing.Import(cfg, body.GetValueOrDefault("config") ?? ""); }
                    catch (InvalidDataException ex) { log.LogWarning("UI: restore refused: {Msg}", ex.Message); await SendJson(res, 400, new { error = "הקובץ הזה הוא לא גיבוי הגדרות תקין של Geder." }); break; }
                    cfg.ReleasedSnapshot = null;
                    store.Save(cfg);
                    log.LogInformation("UI: settings restored from a backup");
                    await SendJson(res, 200, State(cfg, who));
                    break;
                }
                case ("/api/logs/export", "GET"):
                {
                    var zip = DiagnosticsExport.Build(store.Load(), accessLog);
                    res.Headers["Content-Disposition"] = $"attachment; filename=\"geder-logs-{DateTime.Now:yyyyMMdd-HHmm}.zip\"";
                    await Send(res, 200, "application/zip", zip);
                    break;
                }
                case ("/api/state", "GET"):
                    await SendJson(res, 200, State(store.Load(), who));
                    break;
                case ("/api/settings", "POST"):
                {
                    var cfg = store.Load();
                    var view = UserScopes.View(cfg, who);
                    var key = body.GetValueOrDefault("key") ?? "";
                    var turningOn = body.GetValueOrDefault("value") == "true";
                    if (key == "wifi.knownOnly" && turningOn)
                    {
                        // The networks saved on this computer right now become the allowed ones.
                        var saved = WifiGuard.SavedNetworks();
                        if (saved is null) { await SendJson(res, 400, new { error = "לא נמצא במחשב חיבור Wi-Fi." }); break; }
                        foreach (var s in saved) if (!cfg.Wifi.Allowed.Contains(s)) cfg.Wifi.Allowed.Add(s);
                        if (cfg.Wifi.Allowed.Count == 0) { await SendJson(res, 400, new { error = "אין במחשב רשתות Wi-Fi שמורות. התחברו קודם לרשת הבית, ואז הפעילו." }); break; }
                    }
                    if (!ConfigEditing.TrySetToggle(view, key, turningOn, out var err))
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
                    var listValue = (body.GetValueOrDefault("value") ?? "").Trim().Trim('"');
                    if (body.GetValueOrDefault("list") == "allowApps" && body.GetValueOrDefault("action") == "add" && listValue.Contains('\\'))
                    {
                        // A folder: its programs are scanned and allowed one by one. A single program: pinned by contents where needed.
                        if (Directory.Exists(listValue))
                        {
                            var found = FolderScan.Scan(listValue);
                            if (found.Count == 0) { await SendJson(res, 400, new { error = "לא נמצאו תוכנות בתיקייה הזו." }); break; }
                            ConfigEditing.AllowScanned(view, listValue, found);
                        }
                        else if (!AllowOne(view, listValue, out var e1)) { await SendJson(res, 400, new { error = e1 }); break; }
                        UserScopes.Commit(cfg, view);
                        store.Save(cfg);
                        await SendJson(res, 200, State(cfg, who));
                        break;
                    }
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
                    ConfigEditing.SetUrlMode(view.Network, mode);
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

    /// <summary>Allows one program by its full path; outside Program Files it is also pinned by its contents.</summary>
    private static bool AllowOne(GuardianConfig view, string exe, out string error)
    {
        error = "";
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) { error = "התוכנה או התיקייה לא נמצאו במחשב."; return false; }
        if (ConfigEditing.IsAdminOnlyFolder(exe, FolderScan.AdminRoots)) return ConfigEditing.TryEditAppList(view, "allowApps", "add", exe, out error);
        var hash = FolderScan.Hash(exe);
        if (hash is null) { error = "אי אפשר לקרוא את התוכנה."; return false; }
        view.Apps.Rules.RemoveAll(r => r.Action == RuleAction.Allow && string.Equals(r.PathPrefix, exe, StringComparison.OrdinalIgnoreCase));
        view.Apps.Rules.Add(new AppRule { Action = RuleAction.Allow, PathPrefix = exe, Sha256 = hash });
        return true;
    }

    private static object State(GuardianConfig cfg, string? who)
    {
        var c = UserScopes.View(cfg, who);
        return StateOf(c, UserScopes.Find(cfg, who)?.UserName, cfg.UserPolicies.Select(p => p.UserName).ToList(), AllOwn(cfg), cfg.ReleasedSnapshot != null);
    }

    private static List<string> _accounts = [];
    private static DateTime _accountsAt = DateTime.MinValue;

    /// <summary>True when every account on the computer has settings of its own (the defaults then apply to nobody).</summary>
    private static bool AllOwn(GuardianConfig cfg)
    {
        if (cfg.UserPolicies.Count == 0) return false;
        if (DateTime.UtcNow - _accountsAt > TimeSpan.FromMinutes(1))
        {
            try { _accounts = AppCatalogScanner.LocalAccounts(); } catch (Exception) { /* keep the last list */ }
            _accountsAt = DateTime.UtcNow;
        }
        return _accounts.Count > 0 && _accounts.All(a => UserScopes.Find(cfg, a) != null);
    }

    private static object StateOf(GuardianConfig c, string? scope, List<string> scopes, bool allOwn, bool released) => new
    {
        scope,
        scopes,
        allOwn,
        released,
        settings = new Dictionary<string, bool>
        {
            ["apps.enabled"] = c.Apps.Enabled,
            ["apps.defaultDeny"] = c.Apps.DefaultDeny,
            ["apps.trustProgramFiles"] = c.Apps.TrustProgramFiles,
            ["apps.blockAdminTools"] = c.Apps.BlockAdminTools,
            ["apps.blockVideo"] = c.Apps.BlockVideoFiles,
            ["browser.noPrivate"] = c.Browsers.NoPrivate,
            ["network.enabled"] = c.Network.Enabled,
            ["network.urlFiltering"] = c.Network.UrlFiltering,
            ["boot.lockdown"] = c.Boot.Lockdown,
            ["isp.required"] = c.IspFilter.Required,
            ["isp.netfree"] = c.IspFilter.Providers.Contains("NetFree", StringComparer.OrdinalIgnoreCase),
            ["isp.etrog"] = c.IspFilter.Providers.Contains("Etrog", StringComparer.OrdinalIgnoreCase),
            ["clock.locked"] = c.Clock.Locked,
            ["vpn.block"] = c.Vpn.Block,
            ["wifi.knownOnly"] = c.Wifi.KnownOnly,
        },
        mode = c.Network.Mode.ToString(),
        lists = new Dictionary<string, List<string>>
        {
            ["domains"] = c.Network.Domains,
            ["noDecrypt"] = c.Network.NoDecryptDomains,
            ["exceptions"] = c.Network.Exceptions,
            ["blockedApps"] = c.Network.BlockedAppPaths,
            ["directApps"] = c.Network.AllowDirectApps,
            ["localAddresses"] = c.Network.LocalAddresses,
            ["allowApps"] = ConfigEditing.SimpleAppList(c, RuleAction.Allow),
            ["blockApps"] = ConfigEditing.SimpleAppList(c, RuleAction.Block),
            // Provider names left by an older version are chosen with the provider boxes now, so they are not listed.
            ["ispNames"] = c.IspFilter.IssuerNames.Where(n => !IspFilterCheck.ProviderNames.Values.Any(v => v.Contains(n, StringComparer.OrdinalIgnoreCase))).ToList(),
            ["ispThumbprints"] = c.IspFilter.Thumbprints,
            ["ispApps"] = c.IspFilter.ClientApps,
        },
        lockouts = c.UserLockouts.Count,
        idleMinutes = c.UiIdleLockMinutes > 0 ? c.UiIdleLockMinutes : 5,
        activityHours = c.ActivityHours,
        idleChoices = GuardianConfig.IdleLockChoices,
    };

    /// <summary>The programs screen: for an account, only shared programs and its own (others' are blocked for it anyway).</summary>
    private static List<CatalogEntry> CatalogFor(List<CatalogApp> apps, GuardianConfig cfg, string? who) =>
        UserPrograms.ForUser(AppCatalog.WithState(apps, UserScopes.View(cfg, who)), who, Native.UserProfiles.Current());

    /// <summary>Carries out delayed changes whose time has come (measured on the trusted clock).</summary>
    private async Task ReplayLoop(CancellationToken ct)
    {
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port}/") };
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
                if (!store.Exists) continue;
                var cfg = store.Load();
                var due = cfg.Cooling.Pending.Where(p => p.DueUtc <= clock.UtcNow).ToList();
                if (due.Count == 0) continue;
                cfg.Cooling.Pending.RemoveAll(p => due.Contains(p));
                store.Save(cfg);
                foreach (var p in due)
                {
                    var msg = new HttpRequestMessage(HttpMethod.Post, p.Path.TrimStart('/'))
                    {
                        Content = new StringContent(JsonSerializer.Serialize(p.Body, Json), System.Text.Encoding.UTF8, "application/json"),
                    };
                    msg.Headers.Add("X-Guardian", "1");
                    msg.Headers.Add("X-Guardian-Replay", _replaySecret);
                    var r = await client.SendAsync(msg, ct);
                    log.LogInformation("Delayed change {Path} carried out ({Status})", p.Path, (int)r.StatusCode);
                    accessLog.Add("cooling", p.Path, allowed: r.IsSuccessStatusCode, p.User);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { log.LogWarning(ex, "Delayed changes failed"); }
        }
    }

    private bool IsReplay(HttpListenerRequest req) =>
        req.Headers["X-Guardian-Replay"] is { } s && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(s), System.Text.Encoding.ASCII.GetBytes(_replaySecret));

    private bool Authorized(HttpListenerRequest req)
    {
        if (IsReplay(req)) return true;
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

    private static Task Send(HttpListenerResponse res, int status, string type, string text) => Send(res, status, type, Encoding.UTF8.GetBytes(text));

    private static async Task Send(HttpListenerResponse res, int status, string type, byte[] bytes)
    {
        res.StatusCode = status;
        res.ContentType = type;
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }
}
