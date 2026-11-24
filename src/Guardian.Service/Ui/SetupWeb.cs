using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Guardian.Core.Config;
using Guardian.Core.Storage;

namespace Guardian.Service.Ui;

public enum SetupMode { Install, Update, Uninstall }

/// <summary>
/// Install / update / uninstall screens. Served by a one-off listener on a random loopback port,
/// reachable only through a secret path, and shown in Guardian's own window.
/// </summary>
internal sealed class SetupWeb : IDisposable
{
    private readonly HttpListener _http = new();
    private readonly EncryptedConfigStore _store;
    private readonly SetupMode _mode;
    public string Url { get; }
    public bool Succeeded { get; private set; }

    public SetupWeb(EncryptedConfigStore store, SetupMode mode)
    {
        _store = store;
        _mode = mode;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        Url = $"http://127.0.0.1:{FreePort()}/{token}/";
        _http.Prefixes.Add(Url);
        _http.Start();
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (_http.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _http.GetContextAsync(); } catch { return; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private async Task Handle(HttpListenerContext ctx)
    {
        try
        {
            if (ctx.Request.HttpMethod == "GET")
            {
                var page = _mode switch { SetupMode.Install => SetupPages.Install, SetupMode.Update => SetupPages.Update, _ => SetupPages.Uninstall };
                await Send(ctx, 200, "text/html; charset=utf-8", page);
                return;
            }
            if (ctx.Request.Headers["X-Guardian"] != "1") { await Send(ctx, 403, "text/plain", "Forbidden"); return; }
            var body = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(ctx.Request.InputStream) ?? new();
            switch (_mode)
            {
                case SetupMode.Uninstall:
                {
                    var (ok, msg) = Setup.Uninstall(_store, body.GetValueOrDefault("password") ?? "");
                    Succeeded |= ok;
                    await SendJson(ctx, ok ? 200 : 400, new { ok, message = Hebrew(msg) });
                    break;
                }
                case SetupMode.Update:
                {
                    var (ok, msg) = Setup.Upgrade(_store, body.GetValueOrDefault("password") ?? "");
                    Succeeded |= ok;
                    await SendJson(ctx, ok ? 200 : 400, new { ok, message = Hebrew(msg), ui = "http://127.0.0.1:8898/" });
                    break;
                }
                default:
                    if (Setup.IsInstalled()) { await SendJson(ctx, 200, new { ok = true, ui = "http://127.0.0.1:8898/" }); break; }
                    try
                    {
                        int.TryParse(body.GetValueOrDefault("idle"), out var idle);
                        Setup.Install(_store, body.GetValueOrDefault("admin") ?? "", body.GetValueOrDefault("recovery") ?? "", idleLockMinutes: idle);
                        Succeeded = true;
                        await SendJson(ctx, 200, new { ok = true, ui = "http://127.0.0.1:8898/" });
                    }
                    catch (ArgumentException ex) { await SendJson(ctx, 400, new { ok = false, message = Hebrew(ex.Message) }); }
                    break;
            }
        }
        catch (Exception ex)
        {
            try { await SendJson(ctx, 500, new { ok = false, message = ex.Message }); } catch { /* gone */ }
        }
    }

    /// <summary>Hebrew for the messages people can actually hit.</summary>
    private static string Hebrew(string m) => m switch
    {
        "Access denied." or "Wrong password." => "הסיסמה שגויה.",
        "Too many wrong passwords. Try again later." => "יותר מדי ניסיונות שגויים. נסה שוב בעוד 15 דקות.",
        "Guardian is not installed." => "Guardian לא מותקן.",
        "Choose when the settings lock themselves." => "בחר אחרי כמה זמן ההגדרות יינעלו.",
        "The recovery password must differ from the admin password." => "הסיסמה המצילה חייבת להיות שונה מסיסמת הניהול.",
        "Passwords must be at least 8 characters." => "כל סיסמה צריכה להיות באורך 8 תווים לפחות.",
        "Updated. All settings were kept." => "עודכן. כל ההגדרות נשמרו.",
        _ when m.StartsWith("Guardian was removed") => "Guardian הוסר, וכל מה שהוא שינה הוחזר.",
        _ when m.StartsWith("The service did not answer") => "השירות לא ענה וגם הדרך החלופית נכשלה. הפעל מחדש את המחשב ונסה שוב.",
        _ => m,
    };

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static Task SendJson(HttpListenerContext ctx, int status, object value) =>
        Send(ctx, status, "application/json", JsonSerializer.Serialize(value));

    private static async Task Send(HttpListenerContext ctx, int status, string type, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = type;
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        try { _http.Stop(); _http.Close(); } catch { /* already closed */ }
    }
}
