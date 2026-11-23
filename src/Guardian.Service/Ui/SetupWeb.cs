using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Guardian.Core.Storage;

namespace Guardian.Service.Ui;

/// <summary>
/// Install / uninstall screens shown in the browser when the exe is double-clicked.
/// A one-off listener on a random loopback port, reachable only through a secret path,
/// so no other page or program can drive it. Ends when the step is done or after 30 minutes.
/// </summary>
internal static class SetupWeb
{
    public static void Run(EncryptedConfigStore store, bool uninstall)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var port = FreePort();
        var prefix = $"http://127.0.0.1:{port}/{token}/";
        using var http = new HttpListener();
        http.Prefixes.Add(prefix);
        http.Start();
        Setup.OpenUrl(prefix);

        var finished = new ManualResetEventSlim();
        var deadline = DateTime.UtcNow.AddMinutes(30);
        _ = Task.Run(async () =>
        {
            while (!finished.IsSet)
            {
                HttpListenerContext ctx;
                try { ctx = await http.GetContextAsync(); } catch { return; }
                try
                {
                    if (ctx.Request.HttpMethod == "GET")
                    {
                        await Send(ctx, 200, "text/html; charset=utf-8", uninstall ? SetupPages.Uninstall : SetupPages.Install);
                        continue;
                    }
                    if (ctx.Request.Headers["X-Guardian"] != "1") { await Send(ctx, 403, "text/plain", "Forbidden"); continue; }
                    var body = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(ctx.Request.InputStream) ?? new();
                    if (uninstall)
                    {
                        var (ok, msg) = Setup.Uninstall(store, body.GetValueOrDefault("password") ?? "");
                        await SendJson(ctx, ok ? 200 : 400, new { ok, message = msg });
                        if (ok) finished.Set();
                    }
                    else
                    {
                        if (store.Exists) { await SendJson(ctx, 200, new { ok = true, ui = "http://127.0.0.1:8898/" }); finished.Set(); continue; }
                        try
                        {
                            Setup.Install(store, body.GetValueOrDefault("admin") ?? "", body.GetValueOrDefault("recovery") ?? "");
                            await SendJson(ctx, 200, new { ok = true, ui = "http://127.0.0.1:8898/" });
                            finished.Set();
                        }
                        catch (ArgumentException ex) { await SendJson(ctx, 400, new { ok = false, message = ex.Message }); }
                    }
                }
                catch (Exception ex)
                {
                    try { await SendJson(ctx, 500, new { ok = false, message = ex.Message }); } catch { /* gone */ }
                }
            }
        });
        while (!finished.Wait(1000) && DateTime.UtcNow < deadline) { }
        Thread.Sleep(1500); // let the last response reach the browser
    }

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
}
