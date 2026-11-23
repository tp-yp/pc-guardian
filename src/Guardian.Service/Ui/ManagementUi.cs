using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Guardian.Core.Config;
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
public sealed class ManagementUi(ILogger<ManagementUi> log, EncryptedConfigStore store, AdminGate gate, AccessLog accessLog)
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
                _sessions[token] = DateTime.UtcNow.AddMinutes(30);
                res.Headers.Add("Set-Cookie", $"gsid={token}; Path=/; HttpOnly; SameSite=Strict");
                await SendJson(res, 200, new { ok = true });
                return;
            }

            if (!Authorized(req)) { await SendJson(res, 401, new { error = "Login required." }); return; }

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
                case ("/api/state", "GET"):
                    await SendJson(res, 200, State(store.Load()));
                    break;
                case ("/api/settings", "POST"):
                {
                    var cfg = store.Load();
                    if (!ConfigEditing.TrySetToggle(cfg, body.GetValueOrDefault("key") ?? "", body.GetValueOrDefault("value") == "true", out var err))
                    { await SendJson(res, 400, new { error = err }); break; }
                    store.Save(cfg);
                    log.LogInformation("UI: {Key} = {Value}", body["key"], body["value"]);
                    await SendJson(res, 200, State(cfg));
                    break;
                }
                case ("/api/apps/list", "POST"):
                {
                    var cfg = store.Load();
                    if (!ConfigEditing.TryEditAppList(cfg, body.GetValueOrDefault("list") ?? "", body.GetValueOrDefault("action") ?? "",
                            body.GetValueOrDefault("value") ?? "", out var err))
                    { await SendJson(res, 400, new { error = err }); break; }
                    store.Save(cfg);
                    await SendJson(res, 200, State(cfg));
                    break;
                }
                case ("/api/network/list", "POST"):
                {
                    var cfg = store.Load();
                    if (!ConfigEditing.TryEditNetworkList(cfg, body.GetValueOrDefault("list") ?? "", body.GetValueOrDefault("action") ?? "",
                            body.GetValueOrDefault("value") ?? "", out var err))
                    { await SendJson(res, 400, new { error = err }); break; }
                    store.Save(cfg);
                    log.LogInformation("UI: {Action} {Value} in {List}", body["action"], body["value"], body["list"]);
                    await SendJson(res, 200, State(cfg));
                    break;
                }
                case ("/api/network/mode", "POST"):
                {
                    if (!Enum.TryParse<UrlMode>(body.GetValueOrDefault("mode"), out var mode)) { await SendJson(res, 400, new { error = "Bad mode." }); break; }
                    var cfg = store.Load();
                    cfg.Network.Mode = mode;
                    store.Save(cfg);
                    await SendJson(res, 200, State(cfg));
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

    private static object State(GuardianConfig c) => new
    {
        settings = new Dictionary<string, bool>
        {
            ["apps.enabled"] = c.Apps.Enabled,
            ["apps.defaultDeny"] = c.Apps.DefaultDeny,
            ["apps.trustProgramFiles"] = c.Apps.TrustProgramFiles,
            ["network.enabled"] = c.Network.Enabled,
            ["network.urlFiltering"] = c.Network.UrlFiltering,
        },
        mode = c.Network.Mode.ToString(),
        lists = new Dictionary<string, List<string>>
        {
            ["domains"] = c.Network.Domains,
            ["noDecrypt"] = c.Network.NoDecryptDomains,
            ["blockedApps"] = c.Network.BlockedAppPaths,
            ["directApps"] = c.Network.AllowDirectApps,
            ["allowApps"] = ConfigEditing.SimpleAppList(c, RuleAction.Allow),
            ["blockApps"] = ConfigEditing.SimpleAppList(c, RuleAction.Block),
        },
        lockouts = c.UserLockouts.Count,
    };

    private bool Authorized(HttpListenerRequest req)
    {
        var token = req.Cookies["gsid"]?.Value;
        if (token is null || !_sessions.TryGetValue(token, out var exp) || exp < DateTime.UtcNow) return false;
        _sessions[token] = DateTime.UtcNow.AddMinutes(30); // sliding expiry
        return true;
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
