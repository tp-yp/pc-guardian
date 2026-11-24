using System.Net;

namespace Guardian.Service.Ui;

/// <summary>Black-and-white "blocked" screens, in Hebrew: the desktop popup for programs
/// and the page the proxy shows for blocked web addresses.</summary>
public static class BlockScreen
{
    private const string Style = """
<style>
*{box-sizing:border-box}html,body{margin:0;height:100%}
body{background:#000;color:#fff;font-family:"Segoe UI",system-ui,Arial,sans-serif;display:flex;align-items:center;justify-content:center;text-align:center;padding:24px}
.box{max-width:560px;width:100%}
.mark{width:64px;height:64px;border:3px solid #fff;border-radius:50%;margin:0 auto 22px;position:relative}
.mark:after{content:"";position:absolute;left:50%;top:50%;width:46px;height:3px;background:#fff;transform:translate(-50%,-50%) rotate(-45deg)}
h1{font-size:26px;font-weight:600;margin:0 0 10px;letter-spacing:.2px}
.what{font-size:17px;margin:0 0 6px;word-break:break-word;direction:ltr;unicode-bidi:plaintext}
p{color:#bdbdbd;font-size:14px;line-height:1.6;margin:14px 0 0}
button{margin-top:22px;background:#fff;color:#000;border:0;border-radius:6px;padding:9px 28px;font:inherit;font-size:14px;cursor:pointer}
button:focus-visible{outline:2px solid #fff;outline-offset:3px}
</style>
""";

    public static string ForProgram(string name) => $"""
<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><title>חסום</title>{Style}</head><body><div class="box">
<div class="mark" aria-hidden="true"></div>
<h1>התוכנה הזו חסומה</h1>
<div class="what">{WebUtility.HtmlEncode(name)}</div>
<p>Guardian לא מאפשר להפעיל אותה במחשב הזה.<br>אם צריך אותה, אפשר להתיר אותה במסך הניהול עם סיסמת הניהול.</p>
<button onclick="chrome.webview.postMessage('close')" autofocus>הבנתי</button>
</div></body></html>
""";

    public static string ForWebPage(string url) => $"""
<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>האתר חסום</title>{Style}</head><body><div class="box">
<div class="mark" aria-hidden="true"></div>
<h1>הכתובת הזו חסומה</h1>
<div class="what">{WebUtility.HtmlEncode(url.Length > 160 ? url[..160] + "…" : url)}</div>
<p>Guardian חוסם את הדף הזה במחשב הזה.<br>אם צריך אותו, אפשר להתיר אותו במסך הניהול ⇐ פעילות.</p>
</div></body></html>
""";

    public static string NoFilter(string url) => $"""
<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>אין חיבור דרך הסינון</title>{Style}</head><body><div class="box">
<div class="mark" aria-hidden="true"></div>
<h1>אין חיבור דרך הסינון</h1>
<div class="what">{WebUtility.HtmlEncode(url.Length > 160 ? url[..160] + "…" : url)}</div>
<p>המחשב לא מחובר כרגע דרך ספק הסינון (נטפרי / אתרוג / נטספארק), ולכן Guardian חוסם את האינטרנט.<br>כשהחיבור יחזור לעבור דרך הסינון, האינטרנט ייפתח מעצמו תוך פחות מדקה.</p>
</div></body></html>
""";
}
