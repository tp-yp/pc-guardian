using System.Text;
using Guardian.Core.Storage;

namespace Guardian.Service.Ui;

/// <summary>The desktop windows: management app, setup screens, and the "blocked" popup.</summary>
internal static class Gui
{
    public const string AppUrl = "http://127.0.0.1:8898/";

    private const string NotRunning = """
<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><style>
body{font-family:"Segoe UI",system-ui,sans-serif;display:grid;place-items:center;height:100vh;margin:0;background:#f6f7f9;color:#1d2330}
@media (prefers-color-scheme:dark){body{background:#111418;color:#e6e9ee}}
div{text-align:center;max-width:420px;padding:16px}button{margin-top:14px;font:inherit;padding:8px 22px;border:0;border-radius:7px;background:#2563eb;color:#fff;cursor:pointer}
</style></head><body><div><h2>השירות של Guardian לא עונה</h2>
<p>אם הרגע התקנת או עדכנת, זה לוקח כמה שניות. אם זה נמשך, הפעל מחדש את המחשב.</p>
<p style="font-size:13px;opacity:.8">אם ניסית להסיר את Guardian וההסרה לא הסתיימה: הגדרות ווינדוס > אפליקציות > Guardian > הסרת התקנה, שוב. זה ינקה את מה שנשאר.</p>
<button onclick="location.href='http://127.0.0.1:8898/'">לנסות שוב</button></div></body></html>
""";

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    /// <summary>In Safe Mode Windows does not load the networking part the windows rely on, so they cannot open.
    /// Says so plainly instead of showing a window that cannot connect. Protection keeps running there.</summary>
    public static bool InSafeModeNotice()
    {
        if (GetSystemMetrics(67 /* SM_CLEANBOOT */) == 0) return false;
        MessageBoxW(IntPtr.Zero, "המחשב נמצא במצב בטוח. במצב הזה ווינדוס לא טוען חלק מהרכיבים שחלונות Guardian צריכים, ולכן אי אפשר לפתוח כאן את הניהול, ההתקנה או ההסרה.\n\nהפעילו מחדש את המחשב כרגיל ונסו שוב.",
            "Geder", 0x40 | 0x80000 | 0x100000 /* info, right-to-left reading, right-aligned */);
        return true;
    }

    public static int App()
    {
        if (InSafeModeNotice()) return 0;
        using var single = new Mutex(true, "Local\\Guardian.App", out var first);
        if (!first) return 0; // already open
        var shown = AppWindow.Show(new AppWindow.Options("Geder", 1150, 820), wv =>
        {
            wv.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess && wv.Source.StartsWith(AppUrl)) wv.NavigateToString(NotRunning);
            };
            wv.Navigate(AppUrl);
        }, (w, msg) =>
        {
            // "Remove Guardian" in the settings screen: start the uninstaller (it asks for admin
            // rights and the password itself) and close this window.
            if (msg != "uninstall") return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "uninstall-gui") { UseShellExecute = true });
                w.Close();
            }
            catch (System.ComponentModel.Win32Exception) { /* the admin prompt was declined */ }
        });
        if (!shown) NoWebView2(AppUrl);
        return 0;
    }

    public static int Setup(EncryptedConfigStore store, SetupMode mode)
    {
        if (InSafeModeNotice()) return 0;
        using var web = new SetupWeb(store, mode);
        var title = mode switch { SetupMode.Install => "התקנת Geder", SetupMode.Update => "עדכון Geder", _ => "הסרת Geder" };
        var shown = AppWindow.Show(new AppWindow.Options(title, mode == SetupMode.Install ? 1150 : 560, mode == SetupMode.Install ? 820 : 520),
            wv => wv.Navigate(web.Url));
        if (!shown)
        {
            NoWebView2(web.Url);
            // Keep the setup listener alive while the browser is used instead.
            for (var i = 0; i < 1800 && !web.Succeeded; i++) Thread.Sleep(1000);
            Thread.Sleep(2000);
        }
        return web.Succeeded ? 0 : 1;
    }

    public static int Blocked(string base64Name)
    {
        string name;
        try { name = Encoding.UTF8.GetString(Convert.FromBase64String(base64Name)); } catch { name = ""; }
        AppWindow.Show(new AppWindow.Options("Geder", 560, 340, Popup: true, TopMost: true, AutoCloseSeconds: 15),
            wv => wv.NavigateToString(BlockScreen.ForProgram(name)));
        return 0;
    }

    /// <summary>Fallback for the rare Windows without the WebView2 runtime: use the default browser.</summary>
    private static void NoWebView2(string url)
    {
        AppWindow.Message("רכיב התצוגה של ווינדוס (WebView2) לא נמצא, ולכן המסך ייפתח בדפדפן.\nאפשר להתקין אותו מאתר Microsoft (\"WebView2 Runtime\").");
        Guardian.Service.Setup.OpenUrl(url);
    }
}
