namespace Guardian.Service.Ui;

/// <summary>The management app (shown inside the Guardian window). Plain HTML/JS, no external resources.</summary>
internal static class Page
{
    public const string Html = """
<!doctype html>
<html lang="he" dir="rtl">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Guardian</title>
<style>
/* Material 3 look (tokens from a blue seed), light and dark */
:root{--bg:#f9f9ff;--card:#f3f3fa;--card2:#ededf4;--card3:#e2e2e9;--text:#191c20;--muted:#44474e;--ok:#2e6b2f;--okbg:#c8eec0;--bad:#ba1a1a;--badbg:#ffdad6;
 --accent:#415f91;--on-accent:#fff;--accent-c:#d6e3ff;--on-accent-c:#001b3e;--line:#c4c6d0;--outline:#74777f;--warn:#5b4300;--warnbg:#ffefc9;--soft:#dae2f9;--on-soft:#131c2b;
 --inv:#2e3036;--on-inv:#f0f0f7;--shadow:0 1px 2px #0000004d,0 1px 3px 1px #00000026}
@media (prefers-color-scheme:dark){:root{--bg:#111318;--card:#191c20;--card2:#1d2024;--card3:#33353a;--text:#e2e2e9;--muted:#c4c6d0;--ok:#a5d395;--okbg:#1f4d1f;--bad:#ffb4ab;--badbg:#93000a;
 --accent:#aac7ff;--on-accent:#0a305f;--accent-c:#284777;--on-accent-c:#d6e3ff;--line:#44474e;--outline:#8e9099;--warn:#ffdf99;--warnbg:#3f2e00;--soft:#3e4759;--on-soft:#dae2f9;
 --inv:#e2e2e9;--on-inv:#2e3036;--shadow:0 1px 2px #0000004d,0 1px 3px 1px #00000026}}
*{box-sizing:border-box}body{margin:0;font-family:"Segoe UI Variable Text","Segoe UI",system-ui,Arial,sans-serif;background:var(--bg);color:var(--text);font-size:14px;line-height:1.45}
[hidden]{display:none !important}
header{padding:10px 24px;display:flex;justify-content:space-between;align-items:center;gap:12px;flex-wrap:wrap;background:var(--bg);position:sticky;top:0;z-index:5;box-shadow:0 1px 0 var(--line)}
h1{font-size:22px;font-weight:500;margin:0;display:flex;align-items:center;gap:8px}
h1:before{content:"";width:28px;height:28px;border-radius:8px;background:var(--accent) radial-gradient(circle at 50% 58%,var(--on-accent) 0 4px,transparent 5px)}
nav{display:flex;gap:4px;flex-wrap:wrap;background:var(--card2);padding:4px;border-radius:24px}
nav button,.sub button{background:none;color:var(--muted);border:0;border-radius:20px;padding:8px 16px;font-weight:500;height:36px}
nav button:hover,.sub button:hover{background:color-mix(in srgb,var(--text) 8%,transparent)}
nav button.on,.sub button.on{background:var(--soft);color:var(--on-soft)}
.sub{display:flex;gap:4px;margin-bottom:4px;flex-wrap:wrap;background:var(--card2);padding:4px;border-radius:24px;width:fit-content}
main{max-width:1120px;margin:auto;padding:20px;display:grid;gap:16px;grid-template-columns:1fr 1fr}
@media (max-width:820px){main{grid-template-columns:1fr;padding:12px}}
.page{display:contents}
section{background:var(--card);border-radius:16px;padding:20px;min-width:0}
.wide{grid-column:1/-1}
h2{font-size:18px;font-weight:500;margin:0 0 8px}h3{font-size:15px;font-weight:500;margin:14px 0 4px}
p.hint,.hint{color:var(--muted);font-size:13px;margin:0 0 10px;line-height:1.65}
input,select{font:inherit;padding:9px 12px;border-radius:8px;border:1px solid var(--outline);background:var(--bg);color:var(--text);min-height:40px}
input:focus,select:focus{outline:2px solid var(--accent);outline-offset:-1px;border-color:transparent}
input[type=checkbox]{min-height:0;accent-color:var(--accent);width:18px;height:18px}
button{font:inherit;font-weight:500;padding:9px 24px;min-height:40px;border-radius:20px;background:var(--accent);color:var(--on-accent);border:0;cursor:pointer;transition:box-shadow .15s,background .15s}
button:hover{box-shadow:var(--shadow)}button:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
button:disabled{background:color-mix(in srgb,var(--text) 12%,transparent);color:color-mix(in srgb,var(--text) 38%,transparent);box-shadow:none;cursor:not-allowed}
button.link{background:none;color:var(--accent);padding:4px 12px;min-height:32px;border-radius:16px}
button.link:hover{background:color-mix(in srgb,var(--accent) 10%,transparent);box-shadow:none}
button.ghost{background:none;color:var(--accent);border:1px solid var(--outline)}
button.ghost:hover{background:color-mix(in srgb,var(--accent) 8%,transparent);box-shadow:none}
ul{list-style:none;padding:0;margin:8px 0 0;max-height:260px;overflow:auto}
li{display:flex;justify-content:space-between;gap:8px;padding:8px 4px;border-bottom:1px solid var(--line);align-items:center}
li span,.ltr{direction:ltr;word-break:break-all;unicode-bidi:plaintext}
table{width:100%;border-collapse:collapse;font-size:13px}th{text-align:right;color:var(--muted);font-weight:500;padding:8px 6px;border-bottom:1px solid var(--line)}
td{padding:8px 6px;border-bottom:1px solid var(--line);vertical-align:middle}td.l{direction:ltr;text-align:left;word-break:break-all}
tr:hover td{background:color-mix(in srgb,var(--text) 4%,transparent)}
.ok{color:var(--ok)}.bad{color:var(--bad)}.muted{color:var(--muted)}
.row{display:flex;gap:8px;flex-wrap:wrap;align-items:center}
.toggle{display:flex;justify-content:space-between;gap:20px;padding:14px 0;border-bottom:1px solid var(--line);align-items:flex-start}.toggle:last-child{border:0}
.toggle b{display:block;margin-bottom:3px;font-weight:500;font-size:15px}.toggle small{color:var(--muted);line-height:1.6;display:block}
.switch{position:relative;width:52px;height:32px;flex:none;display:inline-block;margin-top:2px}.switch input{opacity:0;width:0;height:0}
.slider{position:absolute;inset:0;background:var(--card3);border:2px solid var(--outline);border-radius:16px;transition:.2s;cursor:pointer}
.slider:before{content:"";position:absolute;width:16px;height:16px;right:6px;top:6px;background:var(--outline);border-radius:50%;transition:.2s}
input:checked+.slider{background:var(--accent);border-color:var(--accent)}
input:checked+.slider:before{width:24px;height:24px;top:2px;right:2px;transform:translateX(-20px);background:var(--on-accent)}
input:focus-visible+.slider{outline:2px solid var(--accent);outline-offset:2px}
input:disabled+.slider{opacity:.38;cursor:not-allowed}
.warn{background:var(--warnbg);color:var(--warn);border-radius:12px;padding:10px 14px;font-size:13px;margin-top:8px;line-height:1.6}
.tip{background:var(--soft);color:var(--on-soft);border-radius:12px;padding:12px 14px;font-size:13px;line-height:1.7}
.tip ul{max-height:none;margin:4px 0 0}.tip li{display:list-item;list-style:disc;margin-right:18px;border:0;padding:1px 0}
.badge{font-size:12px;font-weight:500;padding:3px 10px;border-radius:8px;background:var(--soft);color:var(--on-soft);white-space:nowrap}
.badge.ok{background:var(--okbg);color:var(--ok)}.badge.bad{background:var(--badbg);color:var(--bad)}
.acts{display:flex;gap:2px;flex-wrap:wrap}
.scroll{max-height:60vh;overflow:auto}
.win{display:flex;gap:8px;flex-wrap:wrap;align-items:center;padding:10px;border:1px solid var(--line);border-radius:12px;margin-top:8px;background:var(--bg)}
.days label{font-size:12px;display:inline-flex;align-items:center;gap:3px;margin-left:8px}
.user{border-bottom:1px solid var(--line);padding:14px 0}.user:last-child{border:0}
.steps li{display:list-item;border:0;padding:3px 0}
details summary{cursor:pointer;padding:6px 0}
.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(260px,1fr));gap:16px}
.card{border:1px solid var(--line);border-radius:16px;padding:18px;background:var(--bg);cursor:pointer;text-align:start;font:inherit;font-weight:400;color:inherit;display:flex;flex-direction:column;gap:6px;transition:box-shadow .2s,border-color .2s;min-height:0}
.card:hover,.card:focus-visible{box-shadow:var(--shadow);border-color:var(--accent);outline:none}
.card h3{margin:0 0 4px;font-size:18px;font-weight:500;display:flex;gap:8px;align-items:center;flex-wrap:wrap}
.card.pc{background:var(--accent-c);color:var(--on-accent-c);border-color:transparent}
.card.pc .st.off,.card.pc small{color:inherit;opacity:.75}
.st{font-size:13px;display:flex;gap:8px;align-items:baseline}.st .i{width:16px;text-align:center;flex:none;font-weight:700}
.st.on .i{color:var(--ok)}.st.off{color:var(--muted)}
.card .go{margin-top:auto;padding-top:6px;color:var(--accent);font-size:13px;font-weight:500}
.card.pc .go{color:inherit}
body[data-ctx=user] .machine{display:none!important}
.scopebar{background:var(--accent-c);color:var(--on-accent-c);padding:10px 24px;display:flex;gap:10px;align-items:center;flex-wrap:wrap;font-size:14px}
.scopebar .ghost{color:var(--on-accent-c);border-color:currentColor;background:transparent}
.scopebar.general{background:var(--soft);color:var(--on-soft)}.scopebar.general .ghost{color:var(--on-soft)}
#toast{position:fixed;bottom:20px;left:50%;transform:translateX(-50%);background:var(--inv);color:var(--on-inv);padding:12px 20px;border-radius:8px;font-size:14px;max-width:90vw;z-index:10;box-shadow:var(--shadow)}
#toast.bad{background:var(--badbg);color:var(--bad)}
#login{max-width:400px;margin:15vh auto;padding:0 16px}
</style>
</head>
<body>
<div id="login"><section><h2>כניסה לניהול</h2>
<p class="hint">כדי לשנות הגדרות צריך את סיסמת הניהול (או סיסמת ההצלה).</p>
<form id="lf" class="row"><input id="pw" type="password" placeholder="סיסמה" autofocus required style="flex:1"><button>כניסה</button></form>
<div id="lerr" class="bad" style="margin-top:6px"></div></section></div>

<div id="app" hidden>
<header><h1>Guardian</h1>
<nav id="tabs">
<button data-tab="home" class="on">לוח בקרה</button><button data-tab="features">יכולות</button><button data-tab="programs">תוכנות</button><button data-tab="net">רשת</button><button data-tab="users" id="usersTab">משתמשים</button><button data-tab="log">פעילות</button><button data-tab="settings" class="machine">הגדרות</button>
</nav>
<button class="ghost" id="logout">נעילה</button></header>
<div id="scopebar" class="scopebar general" hidden><span id="scopeText"></span><button class="ghost" id="backHome" style="margin-inline-start:auto">חזרה ללוח הבקרה</button></div>

<main>
<!-- ===== לוח בקרה ===== -->
<div class="page" data-page="home">
<section class="wide"><h2>לוח בקרה</h2>
<p class="hint">חלונית לכל משתמש במחשב, עם ההגנות שפועלות עליו עכשיו. לחיצה על משתמש פותחת את הניהול שלו: תוכנות, אתרים, שעות ופעילות, בנפרד מכל השאר. "המחשב כולו" מרכז את מה שחל על כל המחשב יחד, כמו חסימת מצב בטוח, אינטרנט רק דרך הסינון ונעילת השעון.</p>
<div id="cards" class="cards"></div></section>
</div>

<!-- ===== יכולות ===== -->
<div class="page" data-page="features" hidden>
<section class="wide"><h2>מה פעיל עכשיו</h2>
<p class="hint">מומלץ להדליק יכולת אחת, לבדוק שהמחשב עובד כרגיל, ורק אז להמשיך לבאה. כל שינוי נכנס לתוקף תוך כמה שניות, וכיבוי מחזיר את המצב לקדמותו.</p>
<div id="toggles"></div>
<div id="bootStatus" class="machine" hidden style="margin-top:8px"></div>
<div id="ispConfirm" class="warn" hidden style="margin-top:10px">
<b>לפני שמפעילים "אינטרנט רק דרך הסינון":</b>
<div id="ispNow" style="margin:6px 0">בודק את החיבור…</div>
<ul style="margin:6px 0;padding-inline-start:20px;max-height:none">
<li style="display:list-item;border:0;padding:0">אם Guardian לא מזהה את הסינון, <b>כל האינטרנט במחשב ייחסם</b> מיד, וייפתח מעצמו רק כשהחיבור יעבור שוב דרך הסינון.</li>
<li style="display:list-item;border:0;padding:0">גם כשאין רשת בכלל, או כשהבדיקה נכשלת, האינטרנט נשאר חסום עד שהסינון מזוהה.</li>
<li style="display:list-item;border:0;padding:0">כיבוי המתג או הסרת Guardian (שניהם דורשים סיסמה) מחזירים את האינטרנט מיד.</li>
</ul>
<label style="display:flex;gap:6px;align-items:flex-start"><input type="checkbox" id="ispAgree"> הבנתי, ואני רוצה להפעיל</label>
<div class="row" style="margin-top:8px"><button id="ispGo" disabled>להפעיל</button><button class="ghost" id="ispCancel">ביטול</button></div>
</div>
<div id="bootConfirm" class="warn" hidden style="margin-top:10px">
<b>לפני שמפעילים את חסימת מצב בטוח וסביבת השחזור:</b>
<ul style="margin:6px 0;padding-inline-start:20px;max-height:none">
<li style="display:list-item;border:0;padding:0">לא יהיה אפשר להיכנס ל"הפעלה מחדש מתקדמת" (Shift+הפעלה מחדש), לסביבת השחזור או למצב בטוח, גם לא דרך msconfig.</li>
<li style="display:list-item;border:0;padding:0">אם ווינדוס יתקלקל ולא יעלה, <b>לא יהיה תיקון אוטומטי</b>. כדי לתקן יצטרכו דיסק און קי עם התקנת ווינדוס. כדאי להכין אחד מראש (כלי "Media Creation Tool" של Microsoft).</li>
<li style="display:list-item;border:0;padding:0">כיבוי המתג או הסרת Guardian (שניהם דורשים סיסמה) מחזירים הכול בדיוק למצב הקודם.</li>
<li style="display:list-item;border:0;padding:0">אתחול מדיסק חיצוני לא נחסם. לשם כך צריך לנעול את ה-BIOS (הסבר למטה).</li>
</ul>
<label style="display:flex;gap:6px;align-items:flex-start"><input type="checkbox" id="bootAgree"> הבנתי, ואני רוצה להפעיל את החסימה</label>
<div class="row" style="margin-top:8px"><button id="bootGo" disabled>להפעיל את החסימה</button><button class="ghost" id="bootCancel">ביטול</button></div>
</div></section>
<section class="wide machine" id="ispSec"><h2>אינטרנט רק דרך הסינון (נטפרי / אתרוג / נטספארק)</h2>
<p class="hint">ספקי סינון כמו נטפרי ואתרוג חותמים על האתרים המוצפנים בתעודת אבטחה משלהם. Guardian מתחבר כל דקה לכמה אתרים מוכרים ובודק מי חתם על התעודה. אם החתימה של ספק הסינון, האינטרנט פתוח. אם לא (למשל חיבור לרשת אחרת, נקודה חמה מהטלפון או רשת אורחים), וכשהמתג "אינטרנט רק דרך הסינון" דולק, Guardian חוסם את כל האינטרנט במחשב עד שהחיבור חוזר לעבור דרך הסינון.</p>
<div class="row" style="margin-top:6px"><b>הספק שלי:</b><label><input type="checkbox" id="ispNetfree"> נטפרי</label><label><input type="checkbox" id="ispEtrog"> אתרוג (מבוסס נטספארק)</label></div>
<div id="ispNoProvider" class="warn" hidden>לא נבחר אף ספק ואין תעודה שסומכים עליה, לכן הסינון לעולם לא יזוהה.</div>
<div id="ispStatus" style="margin:8px 0"></div>
<div class="row"><button class="ghost" id="ispCheck">בדיקה עכשיו</button><button class="ghost" id="ispTrust">זה הסינון שלי, לסמוך על התעודה הזו</button></div>
<div id="ispTrustNote" class="warn" hidden>לסמוך רק אם השם שמופיע למעלה הוא של ספק הסינון שלכם. אם תסמכו על תעודה של אתר רגיל, Guardian יחשוב שכל חיבור מסונן. <button id="ispTrustGo">כן, זה הסינון שלי</button> <button class="ghost" id="ispTrustCancel">ביטול</button></div>
<details style="margin-top:10px"><summary><b>הגדרות מתקדמות: איך מזהים את הסינון</b></summary>
<div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:16px;margin-top:10px">
<div><h3>שמות של ספקי סינון</h3><p class="hint">שמות נוספים, מעבר לספקים שסומנו למעלה. אם אחד מהם מופיע בשם של מי שחתם על התעודה, החיבור נחשב מסונן.</p>
<form class="row add" data-list="ispNames"><input required style="flex:1" placeholder="שם של ספק אחר"><button>הוספה</button></form><ul id="ispNames"></ul></div>
<div><h3>תעודות שסומכים עליהן</h3><p class="hint">טביעות אצבע (Thumbprint) של תעודות הסינון. נוסף כאן אוטומטית כשלוחצים "זה הסינון שלי". זו הדרך המדויקת ביותר.</p>
<form class="row add" data-list="ispThumbprints"><input required style="flex:1;direction:ltr" placeholder="A1B2C3…"><button>הוספה</button></form><ul id="ispThumbprints"></ul></div>
<div><h3>תוכנות של ספק הסינון</h3><p class="hint">רק אם ספק הסינון דורש תוכנה במחשב שרצה כשירות: היא תוכל להתחבר גם כשהאינטרנט חסום. נתיב מלא לקובץ.</p>
<form class="row add" data-list="ispApps"><input required style="flex:1" placeholder="C:\Program Files\Filter\client.exe"><button>הוספה</button></form><ul id="ispApps"></ul></div>
</div></details></section>
<section class="wide machine" id="wifiSec"><h2>רשתות Wi-Fi מותרות</h2>
<p class="hint">כש"רק רשתות Wi-Fi מוכרות" דולק, המחשב מתחבר רק לרשתות שברשימה הזו. ההגנה משתמשת במסנני הרשת של ווינדוס עצמו, וחלה על כל המשתמשים. Guardian מחזיר את המסננים אם מישהו מוחק אותם. כיבוי או הסרה של Guardian מחזירים הכול כמו שהיה.<br>
<b>מה זה לא חוסם:</b> חיבור בכבל רשת וחיבור טלפון בכבל USB (שיתוף אינטרנט ב-USB). את אלה עוצר "אינטרנט רק דרך הסינון".</p>
<ul id="wifiAllowed"></ul>
<div class="row" style="margin-top:8px"><select id="wifiSaved"></select><button class="ghost" id="wifiAdd">הוספה לרשימה</button></div>
<div class="hint" id="wifiNote" style="margin-top:6px"></div>
</section>
<section class="wide machine"><h2>אנטי-וירוס עם חומת אש משלו</h2>
<p class="hint">Guardian משתמש בחומת האש של ווינדוס כדי לחסום אינטרנט: לתוכנות שסימנתם "בלי אינטרנט", כשאין חיבור דרך הסינון, ולתוכנות שלא עוברות בסינון לפי כתובת מלאה. יש אנטי-וירוסים (למשל ESET, Kaspersky, Norton, Bitdefender) שמביאים חומת אש משלהם ומכבים את זו של ווינדוס. במצב כזה:</p>
<ul class="hint steps" style="line-height:1.8;margin:0;padding-inline-start:22px">
<li><b>לא יעבדו:</b> "בלי אינטרנט" לתוכנות, "אינטרנט רק דרך הסינון", וחסימת תוכנות שעוקפות את הסינון לפי כתובת מלאה.</li>
<li><b>ימשיכו לעבוד:</b> נעילת תוכנות, שעות כניסה, חסימת אתרים דרך בירור הכתובות, והסינון לפי כתובת מלאה בדפדפנים.</li>
<li>הסריקה של אתרים מוצפנים שיש בחלק מהאנטי-וירוסים יכולה גם להתנגש עם הסינון לפי כתובת מלאה ולגרום לשגיאות אבטחה באתרים.</li>
<li><b>מומלץ:</b> להשתמש ב-Microsoft Defender, שמובנה בווינדוס, או לכבות את רכיב חומת האש באנטי-וירוס ולהשאיר את חומת האש של ווינדוס.</li>
</ul>
<div id="fwWarn" class="warn" hidden style="margin-top:8px"></div>
</section>
<section class="wide machine"><h2>נעילת ה-BIOS (מומלץ מאוד כשמשתמשים בחסימת מצב בטוח)</h2>
<p class="hint">Guardian שומר על ווינדוס מבפנים. מי שמפעיל את המחשב מדיסק און קי (התקנת ווינדוס או "דיסק טכנאים") עוקף כל תוכנה, וזה נחסם רק בהגדרות המחשב עצמו (BIOS/UEFI). ההוראות שונות מעט בכל יצרן:</p>
<ol class="hint steps" style="line-height:1.8;margin:0;padding-inline-start:22px">
<li>כדי להיכנס ל-BIOS: <b>הגדרות ווינדוס > מערכת > שחזור > הפעלה מתקדמת > הפעל מחדש עכשיו > פתרון בעיות > אפשרויות מתקדמות > הגדרות קושחת UEFI</b>. אם חסימת מצב בטוח כבר דולקת, צריך במקום זה ללחוץ בזמן ההדלקה על F2, Del, F10 או Esc, לפי היצרן.</li>
<li><b>סיסמת מנהל ל-BIOS</b> (Supervisor / Administrator Password): בלשונית Security. שמרו אותה במקום בטוח. אם שוכחים אותה, לפעמים רק מעבדה יכולה לאפס.</li>
<li><b>סדר אתחול</b> (Boot): להשאיר רק את הדיסק הפנימי (Windows Boot Manager), ולכבות אתחול מ-USB ומהרשת (USB Boot, PXE / Network Boot).</li>
<li><b>תפריט אתחול חד-פעמי</b> (Boot Menu, בדרך כלל F12): לכבות אותו, או לוודא שהוא דורש את הסיסמה.</li>
<li><b>Secure Boot</b>: להשאיר דולק (Enabled).</li>
<li>שמירה ויציאה: בדרך כלל F10.</li>
</ol>
<p class="hint" style="margin-top:8px">גם אחרי כל זה, מי שפותח את המחשב פיזית יכול לאפס את ה-BIOS או להוציא את הדיסק. זו הגבלה של כל מחשב.</p>
</section>
</div>

<!-- ===== תוכנות ===== -->
<div class="page" data-page="programs" hidden>
<section class="wide"><h2>כל התוכנות במחשב</h2>
<p class="hint">הרשימה נאספת מתפריט התחל, מ"אפליקציות מותקנות" בהגדרות ומהחנות של Microsoft. לכל תוכנה אפשר לבחור:<br>
<b>נעולה</b> – התוכנה תיסגר מיד בכל פעם שמישהו ינסה לפתוח אותה, ויופיע מסך חסימה.<br>
<b>בלי אינטרנט</b> – התוכנה תיפתח כרגיל, אבל לא תוכל להתחבר לאינטרנט.<br>
<b>שעות</b> – התוכנה תיחסם רק בשעות שתקבעו (למשל משחק שמותר רק אחרי הצהריים). מחוץ לשעות האלה היא עובדת כרגיל, ואם היא פתוחה כשהשעה מגיעה, היא נסגרת.<br>
<b>גישה ישירה</b> – רלוונטי רק כש"סינון לפי כתובת מלאה" דולק: התוכנה תתחבר לאינטרנט בלי לעבור בסינון. מתאים למשחקים, Steam, Spotify ותוכנות שמפסיקות לעבוד כשהסינון דולק.</p>
<div class="row"><input id="q" placeholder="חיפוש תוכנה…" style="flex:1;min-width:180px"><label><input type="checkbox" id="onlySet"> רק תוכנות עם הגבלה</label><button class="ghost" id="refresh">רענון הרשימה</button></div>
<div id="catNote" class="hint" style="margin-top:8px"></div>
<div class="scroll"><table><thead><tr><th>תוכנה</th><th>מקור</th><th>נעולה</th><th>בלי אינטרנט</th><th>גישה ישירה</th><th>שעות חסימה</th></tr></thead><tbody id="catalog"></tbody></table></div>
</section>
<section class="wide"><details><summary><b>הוספה ידנית (לתוכנות שלא מופיעות ברשימה, למשל קבצים ניידים)</b></summary>
<div style="display:grid;grid-template-columns:1fr 1fr;gap:16px;margin-top:10px">
<div><h3>תוכנות מותרות</h3><p class="hint">כשהיכולת "חסימת כל תוכנה לא מוכרת" דולקת, רק תוכנות מוכרות נפתחות. כאן מוסיפים תוכנה או תיקייה שמותר להפעיל. אפשר לכתוב שם קובץ (zoom.exe) או תיקייה שלמה (C:\Tools\).</p>
<form class="row add" data-list="allowApps"><input required style="flex:1" placeholder="zoom.exe או C:\Tools\"><button>הוספה</button></form><ul id="allowApps"></ul></div>
<div><h3>תוכנות נעולות</h3><p class="hint">תוכנות שייסגרו תמיד, גם אם הותקנו כרגיל. שם קובץ (game.exe) או נתיב מלא.</p>
<form class="row add" data-list="blockApps"><input required style="flex:1" placeholder="game.exe"><button>הוספה</button></form><ul id="blockApps"></ul></div>
</div></details></section>
</div>

<!-- ===== רשת ===== -->
<div class="page" data-page="net" hidden>
<div class="wide sub" id="netsub"><button data-sub="urls" class="on">חסימת כתובות</button><button data-sub="netapps">תוכנות ברשת</button></div>

<div class="page" data-sub-page="urls">
<section class="wide"><h2>איך לחסום אתרים</h2>
<div class="row" style="margin-bottom:8px"><select id="mode"><option value="Blocklist">רשימה שחורה – הכול מותר, חוץ ממה שברשימה</option><option value="Allowlist">רשימה לבנה – הכול חסום, חוץ ממה שברשימה</option></select></div>
<p class="hint" id="modeHint"></p>
<div class="row" style="margin:8px 0"><button class="ghost" id="msUpdates">להתיר עדכוני ווינדוס ואופיס</button><span class="hint">פותח את כתובות העדכונים של Microsoft (גם ברשימה לבנה) ונותן גישה ישירה לתוכנת העדכון של Office.</span></div>
<div id="netOff" class="warn" hidden>היכולת "סינון אתרים וחומת אש" כבויה כרגע, ולכן הרשימות כאן לא נאכפות. אפשר להדליק אותה בלשונית "יכולות".</div>
<div id="urlOff" class="warn" hidden>"סינון לפי כתובת מלאה" כבוי, לכן כתובות עם נתיב (למשל youtube.com/shorts) לא נאכפות. רק אתרים שלמים נחסמים.</div>
</section>
<section><h2 id="domainsTitle">רשימה</h2>
<p class="hint">אפשר לכתוב אתר שלם (tiktok.com, וזה כולל גם www.tiktok.com) או חלק מאתר (youtube.com/shorts).</p>
<form class="row add" data-list="domains"><input placeholder="tiktok.com או youtube.com/shorts" required style="flex:1"><button>הוספה</button></form>
<ul id="domains"></ul></section>
<section><h2>יוצאי דופן</h2>
<p class="hint" id="excHint"></p>
<form class="row add" data-list="exceptions"><input placeholder="youtube.com/watch?v=..." required style="flex:1"><button>הוספה</button></form>
<ul id="exceptions"></ul></section>
<section class="wide machine"><h2>אתרים שלא נבדקים לעומק (בנקים ואתרים רגישים)</h2>
<p class="hint">כדי לראות כתובת מלאה, Guardian פותח את התקשורת המוצפנת במחשב שלך (שום דבר לא יוצא מהמחשב). באתרים שברשימה הזו הוא לא עושה את זה, ובודק רק את שם האתר. מומלץ להוסיף כאן בנקים, קופות חולים ואתרים רשמיים. אם אתר מסוים מציג שגיאת אבטחה, הוסיפו אותו לכאן.</p>
<form class="row add" data-list="noDecrypt"><input placeholder="bankhapoalim.co.il" required style="flex:1"><button>הוספה</button></form>
<ul id="noDecrypt"></ul></section>
</div>

<div class="page" data-sub-page="netapps" hidden>
<section><h2>תוכנות בלי אינטרנט</h2>
<p class="hint">התוכנות האלה נפתחות כרגיל אבל לא יכולות להתחבר לאינטרנט. הדרך הנוחה להוסיף היא בלשונית "תוכנות". כאן אפשר גם להדביק נתיב מלא לקובץ.</p>
<form class="row add" data-list="blockedApps"><input required style="flex:1" placeholder="C:\Games\game.exe"><button>הוספה</button></form><ul id="blockedApps"></ul></section>
<section class="machine"><h2>תוכנות עם גישה ישירה</h2>
<p class="hint">כש"סינון לפי כתובת מלאה" דולק, כל התקשורת עוברת דרך מסנן אחד במחשב. רוב הדפדפנים עוברים בו בלי בעיה, אבל יש תוכנות שלא יודעות לעבור דרכו ולכן מאבדות אינטרנט. תוכנות ברשימה הזו מתחברות ישירות, בלי סינון.</p>
<form class="row add" data-list="directApps"><input required style="flex:1" placeholder="C:\Program Files (x86)\Steam\steam.exe"><button>הוספה</button></form><ul id="directApps"></ul></section>
<section class="wide machine"><h2>תוכנות שניסו להתחבר ונחסמו</h2>
<p class="hint">במקום ניסוי וטעייה: Guardian רושם כל תוכנה שניסתה להתחבר לאינטרנט ישירות, בלי לעבור במסנן, ונחסמה. אם תוכנה שאתם צריכים מופיעה כאן ולא עובדת, לחצו "לאפשר גישה ישירה". הרשימה מתמלאת רק כש"סינון לפי כתובת מלאה" דולק.</p>
<div class="scroll"><table><thead><tr><th>תוכנה</th><th>ניסיונות</th><th>לאחרונה</th><th></th></tr></thead><tbody id="direct"></tbody></table></div>
<div id="directEmpty" class="hint" style="margin-top:8px"></div>
</section>
<section class="wide machine"><h2>מה בדרך כלל עובד דרך המסנן ומה לא</h2>
<div class="tip">זו הערכה כללית, וגרסאות משתנות. הרשימה למעלה מראה מה קורה בפועל במחשב שלך.
<ul>
<li><b>עובדים דרך המסנן:</b> Edge, Chrome, Brave, Opera, רוב תוכנות Office, Teams, Zoom, Windows Update, החנות של Microsoft, Dropbox, OneDrive.</li>
<li><b>Firefox:</b> עובד, בתנאי שבהגדרות הרשת שלו נבחר "השתמש בהגדרות ה-proxy של המערכת" (זו ברירת המחדל).</li>
<li><b>בדרך כלל צריכים גישה ישירה:</b> Steam ומשחקים מקוונים, Epic Games, Spotify, Discord (קול ווידאו), WhatsApp ו-Telegram למחשב, תוכנות VPN, תוכנות שיתוף מסך כמו AnyDesk ו-TeamViewer.</li>
<li>תוכנה שמקבלת גישה ישירה לא מסוננת בכלל. אם המטרה היא לחסום אותה, השתמשו ב"בלי אינטרנט" או ב"נעולה".</li>
</ul></div></section>
</div>
</div>

<!-- ===== משתמשים ===== -->
<div class="page" data-page="users" hidden>
<section class="wide"><h2>שעות כניסה למשתמשים</h2>
<p class="hint"><b>מניעת התמכרות:</b> אחרי זמן השימוש הרציף שתבחרו, המשתמש מקבל התראה ואז יוצא מהחשבון. הוא יכול להיכנס שוב מיד, והספירה מתחילה מחדש. זה יוצר הפסקה קטנה ורגע של מחשבה, בלי לחסום לגמרי.</p>
<p class="hint">לכל משתמש במחשב אפשר לקבוע שעות שבהן אסור לו להיות מחובר. כשמגיעה השעה, המשתמש מקבל הודעה כמה דקות לפני, ואז הוא מנותק והמחשב חוזר למסך הכניסה. בזמן הזה הוא גם לא יכול להיכנס.<br>
טווח שעות שעובר חצות (למשל 23:00 עד 07:00) נחשב ליום שבו הוא מתחיל. כדי לחסום יום שלם, בחרו אותה שעה בהתחלה ובסוף.</p>
<div id="allLocked" class="warn" hidden>שימו לב: לכל המשתמשים במחשב יש שעות נעילה. אם יש שעה שבה כולם נעולים, אף אחד לא יוכל להיכנס למחשב בזמן הזה, גם לא כדי לשנות הגדרות. כדאי להשאיר לפחות משתמש מנהל אחד בלי נעילה.</div>
<div class="tip machine" style="margin-bottom:10px"><b>ניהול נפרד לכל משתמש:</b> בלוח הבקרה לוחצים על משתמש כדי לנהל את התוכנות, האתרים, השעות והפעילות שלו בנפרד. בכניסה הראשונה הוא מקבל הגדרות משלו, שמתחילות כהעתק של ברירת המחדל. משתמש שלא נכנסו לניהול שלו פועל לפי ברירת המחדל, שנערכת ב"המחשב כולו".</div>
<div id="scopeUrlWarn" class="warn" hidden>יש משתמשים עם הגדרות אתרים נפרדות, אבל "סינון לפי כתובת מלאה" כבוי. בלעדיו אפשר להבדיל בין משתמשים רק בתוכנות. אתר ייחסם רק אם הוא חסום לכל המשתמשים.</div>
<div id="users"></div>
</section>
</div>

<!-- ===== פעילות ===== -->
<div class="page" data-page="log" hidden>
<section class="wide"><h2>פעילות בזמן אמת</h2>
<p class="hint">כל אתר שנפתח וכל תוכנה שנחסמה מופיעים כאן. אפשר לחסום או להתיר כל שורה: רק את הכתובת המדויקת, או את כל האתר (הדומיין). לתוכנה שנחסמה אפשר להתיר רק את הקובץ, או את כל התיקייה שלה (מומלץ לתוכנות שמורכבות מכמה קבצים, כמו Discord או Zoom).</p>
<div class="row"><label><input type="checkbox" id="onlyBlocked"> רק מה שנחסם</label><input id="filter" placeholder="סינון…" style="flex:1;min-width:160px"><label><input type="checkbox" id="pause"> עצירת עדכון</label></div>
<div class="scroll" style="margin-top:8px"><table><thead><tr><th>שעה</th><th>משתמש</th><th>סוג</th><th>מצב</th><th>מה</th><th>פעולות</th></tr></thead><tbody id="log"></tbody></table></div></section>
</div>

<!-- ===== הגדרות ===== -->
<div class="page" data-page="settings" hidden>
<section><h2>נעילת הממשק</h2>
<p class="hint">אם לא נוגעים בחלון הזה במשך הזמן שנבחר, הוא ננעל ושוב צריך סיסמה. כך אף אחד לא יכול לשנות הגדרות אם שכחתם את החלון פתוח.</p>
<div class="row"><label>לנעול אחרי <select id="idle"></select> דקות ללא פעילות</label></div></section>
<section><h2>שינוי סיסמה</h2>
<p class="hint">את סיסמת הניהול אפשר לשנות עם סיסמת הניהול או סיסמת ההצלה. את סיסמת ההצלה אפשר לשנות רק עם סיסמת ההצלה עצמה. לפחות 8 תווים, ושתי הסיסמאות צריכות להיות שונות.</p>
<form id="pwf" style="display:grid;gap:8px">
<select id="pwWhich"><option value="admin">סיסמת הניהול</option><option value="recovery">סיסמת ההצלה</option></select>
<input type="password" id="pwCur" required placeholder="הסיסמה הנוכחית" autocomplete="current-password">
<input type="password" id="pwNew" required minlength="8" placeholder="סיסמה חדשה" autocomplete="new-password">
<input type="password" id="pwNew2" required minlength="8" placeholder="הסיסמה החדשה שוב" autocomplete="new-password">
<div><button>שינוי הסיסמה</button></div></form></section>
<section><h2>מצב החיבור</h2>
<p class="hint">כאן רואים אם Guardian מצליח לברר כתובות אתרים. אם יש "מחובר, אין אינטרנט", הסתכלו כאן קודם.</p>
<div id="health" class="hint"></div><button class="ghost" id="healthBtn">בדיקה</button></section>
<section class="wide machine"><h2>השהיית כיבוי</h2>
<p class="hint">כל שינוי שמחליש את ההגנה (כיבוי יכולת, שחרור תוכנה או אתר, הסרת שעות, שחרור זמני, הקטנת ההשהיה עצמה) לא מתבצע מיד, אלא אחרי מספר השעות שתקבעו. בזמן הזה אפשר לבטל אותו כאן. שינויים שמחמירים את ההגנה מתבצעים מיד. גם הסרת Guardian דורשת בקשה מראש, והיא מתאפשרת רק אחרי ההשהיה. הזמן נמדד לפי השעון המהימן של Guardian, כך ששינוי השעון לא מקצר אותו. 0 = בלי השהיה.</p>
<div class="row"><label>השהיה של <input type="number" id="coolHours" min="0" style="width:110px"> שעות</label><button id="coolSave">שמירה</button></div>
<h3 style="margin:14px 0 4px;font-size:15px">שינויים שממתינים</h3>
<ul id="coolPending"></ul>
<div id="coolUninstall" class="row" style="margin-top:10px"></div>
</section>
<section class="wide"><h2>הסרת Guardian</h2>
<p class="hint">מסיר את Guardian מהמחשב ומחזיר את כל מה שהוא שינה: חומת האש, הגדרות הרשת, תעודת האבטחה ושעות הכניסה של המשתמשים. ווינדוס יבקש אישור מנהל, ואחריו צריך להקליד את סיסמת הניהול או את הסיסמה המצילה.</p>
<button class="ghost" id="uninstall" style="color:var(--bad);border-color:var(--bad)">הסרת Guardian…</button>
<div id="uninstallNote" class="hint" hidden style="margin-top:8px">אפשר גם להסיר דרך הגדרות ווינדוס: אפליקציות > אפליקציות מותקנות > Guardian > הסרת התקנה.</div></section>
</div>
</main>
</div>
<div id="toast" hidden></div>

<script>
const $=id=>document.getElementById(id);
const el=(tag,props={},...kids)=>{const e=document.createElement(tag);for(const[k,v]of Object.entries(props)){if(k==='class')e.className=v;else if(k.startsWith('on'))e[k]=v;else if(k in e)e[k]=v;else e.setAttribute(k,v)}e.append(...kids.filter(x=>x!=null));return e};
const HE={'Wrong password.':'הסיסמה שגויה.','Too many attempts. Try again later.':'יותר מדי ניסיונות. נסו שוב בעוד כמה דקות.','Login required.':'צריך להיכנס שוב.','Invalid value.':'הערך לא תקין.','Not in list.':'לא נמצא ברשימה.','Invalid program.':'התוכנה לא נמצאה.','Invalid user name.':'שם המשתמש לא תקין.','Pick at least one day for each time range.':'צריך לבחור לפחות יום אחד בכל טווח שעות.','Warning must be 0-60 minutes.':'זמן ההתראה צריך להיות בין 0 ל-60 דקות.','Internal error.':'שגיאה פנימית. נסו שוב.','Password must be at least 8 characters.':'הסיסמה צריכה להיות באורך 8 תווים לפחות.','Must differ from the recovery password.':'סיסמת הניהול צריכה להיות שונה מסיסמת ההצלה.','Must differ from the admin password.':'סיסמת ההצלה צריכה להיות שונה מסיסמת הניהול.','Only the recovery password can change the recovery password.':'את סיסמת ההצלה אפשר לשנות רק עם סיסמת ההצלה הנוכחית.'};
let idleMin=5,lastAct=Date.now(),lastPing=Date.now(),state=null;
function toast(msg,bad){const t=$('toast');t.textContent=msg;t.className=bad?'bad':'';t.hidden=false;clearTimeout(t._t);t._t=setTimeout(()=>t.hidden=true,4500)}
function api(p,o={},active=true){const h={'X-Guardian':'1','Content-Type':'application/json'};if(active)h['X-Active']='1';
 return fetch(p,{...o,headers:h,credentials:'same-origin'}).then(async r=>{const j=await r.json().catch(()=>({}));if(r.status===401&&p!=='/api/login'){show(false)}
  if(r.status===202&&j.pending){toast('השינוי נשמר ויתבצע ב-'+fmtDue(j.due)+' (השהיית כיבוי של '+j.hours+' שעות). אפשר לבטל אותו בלשונית "הגדרות".');load();loadCooling();if(catalog)renderCatalog();return new Promise(()=>{})}
  if(!r.ok)throw new Error(HE[j.error]||j.error||('שגיאה '+r.status));return j})}
const fmtDue=d=>new Date(d).toLocaleString('he-IL',{day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit'});
let scope='';const SCOPED=['/api/settings','/api/network/msupdates','/api/apps/list','/api/network/list','/api/network/mode','/api/catalog/set','/api/site','/api/apps/allow','/api/tools/unlock','/api/catalog/hours'];
const post=(p,b)=>api(p,{method:'POST',body:JSON.stringify(SCOPED.includes(p)&&!('user' in b)?{...b,user:scope}:b)});
const q=(p,extra)=>p+'?user='+encodeURIComponent(scope)+(extra||'');
const short=u=>u?String(u).split('\\').pop():'';
const scopeName=()=>scope?'של '+scope:'הכלליות';
const fail=x=>toast(x.message,true);

function show(app){$('app').hidden=!app;$('login').hidden=app;if(app){lastAct=Date.now();scope='';load();loadIsp();openTab('home');tick()}else{$('pw').focus()}}
$('lf').onsubmit=e=>{e.preventDefault();post('/api/login',{password:$('pw').value}).then(()=>{$('pw').value='';$('lerr').textContent='';show(true)}).catch(x=>$('lerr').textContent=x.message)};
$('logout').onclick=()=>post('/api/logout',{}).finally(()=>show(false));

// Idle lock: the server also expires the session, this just locks the screen on time.
['mousemove','keydown','click','wheel','touchstart'].forEach(ev=>addEventListener(ev,()=>{lastAct=Date.now();if(!$('app').hidden&&Date.now()-lastPing>60000){lastPing=Date.now();api('/api/state').catch(()=>{})}},{passive:true}));
setInterval(()=>{if(!$('app').hidden&&Date.now()-lastAct>idleMin*60000){post('/api/logout',{}).finally(()=>{show(false);$('lerr').textContent='הממשק ננעל אחרי חוסר פעילות.'})}},10000);

// Tabs
function openTab(name){if(name==='settings'&&scope)name='features';
 document.querySelectorAll('#tabs button').forEach(x=>x.classList.toggle('on',x.dataset.tab===name));document.querySelectorAll('[data-page]').forEach(m=>m.hidden=m.dataset.page!==name);
 $('scopebar').hidden=name==='home';if(name==='home')loadDash();
 if(name==='programs'&&!catalog)loadCatalog(false);if(name==='users')loadUsers();if(name==='net')loadDirect();if(name==='settings'){loadHealth();loadCooling()}if(name==='features'){loadIsp();if(!scope)loadWifi()}}
document.querySelectorAll('#tabs button').forEach(b=>b.onclick=()=>openTab(b.dataset.tab));
document.querySelectorAll('#netsub button').forEach(b=>b.onclick=()=>{document.querySelectorAll('#netsub button').forEach(x=>x.classList.toggle('on',x===b));document.querySelectorAll('[data-sub-page]').forEach(m=>m.hidden=m.dataset.subPage!==b.dataset.sub);if(b.dataset.sub==='netapps')loadDirect()});

// Features
const TOGGLES=[
 ['apps.enabled','נעילת תוכנות','תוכנות שסימנתם כ"נעולות" בלשונית "תוכנות" ייסגרו מיד כשמישהו פותח אותן, ויופיע מסך חסימה. בלי היכולת הזו, הסימונים לא נאכפים.'],
 ['apps.defaultDeny','חסימת כל תוכנה לא מוכרת','כל תוכנה שלא הותקנה כרגיל תיחסם: קבצים שהורדו, תוכנות ניידות, דיסק און קי ותוכנות שמתקינות את עצמן בלי אישור מנהל. כדי לאפשר אחת מהן, התירו אותה מלשונית "פעילות" או הוסיפו אותה ב"תוכנות" > "הוספה ידנית".','לפני שמדליקים: כדאי להדליק קודם את "נעילת תוכנות", לעבוד יום רגיל, ולראות בלשונית "פעילות" מה היה נחסם.'],
 ['apps.trustProgramFiles','לסמוך על תוכנות שהותקנו כרגיל','תוכנות שהותקנו לתיקיית Program Files (התקנה רגילה שדורשת אישור מנהל) נחשבות מוכרות ולא נחסמות. מומלץ להשאיר דולק.'],
 ['apps.blockAdminTools','חסימת כלי מנהל','חוסם את שורת הפקודה, PowerShell, עורך הרישום, מסופי הניהול (שירותים, מתזמן משימות, מדיניות קבוצתית), פקודות הרשאות ושירותים, שחזור מערכת ואיפוס המחשב. כמעט כל הוראה לעקיפת Guardian עוברת דרך הכלים האלה, ולכן זו אחת ההגנות החזקות. גם עותק או שינוי שם של הכלי ייחסמו. מנהל המשימות וההגדרות נשארים פתוחים. כשצריך את הכלים, משחררים אותם לזמן קצוב מתחת למתג הזה.','לפני שמדליקים לחשבון שלכם: ודאו שאתם זוכרים את סיסמת Guardian, כי בלעדיה לא תוכלו לשחרר את הכלים.'],
 ['network.enabled','סינון אתרים וחומת אש','חוסם אתרים שלמים לפי הרשימות בלשונית "רשת", ומונע אינטרנט מתוכנות שסימנתם "בלי אינטרנט". עובד בכל הדפדפנים ובכל התוכנות.'],
 ['isp.required','אינטרנט רק דרך הסינון','חוסם את כל האינטרנט כשהמחשב לא מחובר דרך ספק סינון (נטפרי, אתרוג או נטספארק). הזיהוי לפי תעודת האבטחה, והסבר מלא בהמשך העמוד. ההפעלה דורשת אישור, וכיבוי מחזיר את האינטרנט מיד.'],
 ['vpn.block','חסימת VPN ופרוקסי','חוסם תוכנות VPN ופרוקסי מוכרות (גם אם שינו להן את השם), את אתרי ההורדה שלהן ואתרי פרוקסי מוכרים, את החיבורים של ה-VPN המובנה בווינדוס, ואת האפשרות להגדיר פרוקסי ידנית. ה-VPN המובנה מסוג SSTP משתמש באותו חיבור כמו אתרים רגילים ולא נחסם כאן, וגם תוכנה שלא ברשימה לא תיחסם לפי השם. לכן כדאי להפעיל גם "סינון לפי כתובת מלאה" או "חסימת כל תוכנה לא מוכרת".'],
 ['wifi.knownOnly','רק רשתות Wi-Fi מוכרות','המחשב יוכל להתחבר רק לרשתות Wi-Fi שכבר שמורות בו עכשיו. רשתות חדשות, כמו נקודה חמה מטלפון או רשת של שכנים, לא יופיעו ולא יהיה אפשר להתחבר אליהן. הרשימה המלאה ואפשרות להוסיף רשת נמצאות בהמשך העמוד.'],
 ['clock.locked','נעילת השעון','Guardian לוקח את השעה הנכונה מהאינטרנט וסופר אותה בעצמו, כך ששינוי השעה או אזור הזמן במחשב לא עוקף את שעות הנעילה ואת התוכנות שחסומות לפי שעות. אם מישהו משנה את השעון, הוא חוזר לשעה הנכונה תוך כמה שניות.'],
 ['boot.lockdown','חסימת מצב בטוח וסביבת השחזור','מונע עקיפה של Guardian דרך מצב בטוח, "הפעלה מחדש מתקדמת" (Shift+הפעלה מחדש) וסביבת השחזור של ווינדוס. תיקון אוטומטי ותפריט F8 מכובים גם הם. ההפעלה דורשת אישור, והכיבוי או ההסרה מחזירים הכול כמו שהיה.'],
 ['network.urlFiltering','סינון לפי כתובת מלאה','מאפשר לחסום חלק מאתר (למשל רק youtube.com/shorts) ולא רק אתר שלם. כדי לעשות את זה Guardian מתקין תעודת אבטחה מקומית ובודק את התקשורת המוצפנת במחשב עצמו. יש תוכנות שלא יודעות לעבוד כך, והן יאבדו אינטרנט עד שתאפשרו להן "גישה ישירה" (רשת > תוכנות ברשת יראה לכם בדיוק אילו).','אחרי ההדלקה צריך לסגור ולפתוח מחדש את הדפדפנים. בדקו את "רשת" > "תוכנות ברשת" בימים הראשונים.'],
];
function sw(checked,onchange,label,disabled){const i=el('input',{type:'checkbox',checked,disabled:!!disabled});i.setAttribute('aria-label',label);i.onchange=()=>onchange(i);return el('label',{class:'switch',title:label},i,el('span',{class:'slider'}))}
const PER_USER=['apps.enabled','apps.defaultDeny','apps.trustProgramFiles','apps.blockAdminTools'];
function askBoot(){$('bootConfirm').hidden=false;$('bootAgree').checked=false;$('bootGo').disabled=true;$('bootConfirm').scrollIntoView({behavior:'smooth',block:'center'})}
$('bootAgree').onchange=()=>$('bootGo').disabled=!$('bootAgree').checked;
$('bootCancel').onclick=()=>$('bootConfirm').hidden=true;
$('bootGo').onclick=()=>post('/api/settings',{key:'boot.lockdown',value:'true'}).then(st=>{$('bootConfirm').hidden=true;render(st);setTimeout(loadBoot,8000);toast('חסימת מצב בטוח וסביבת השחזור הופעלה. מומלץ לנעול גם את ה-BIOS.')}).catch(fail);
// Filtered line (ISP filter)
const ISPST={Filtered:['ok','החיבור עובר דרך הסינון'],NotFiltered:['bad','החיבור לא עובר דרך הסינון'],NoNetwork:['bad','אין חיבור לאינטרנט'],Unknown:['muted','עוד לא נבדק']};
let isp=null;
function ispLine(i){const[c,t]=ISPST[i.status]||ISPST.Unknown;return el('div',{},el('b',{class:c},t),i.signer?el('span',{},' · התעודה נחתמה על ידי: ',el('b',{style:'direction:ltr;unicode-bidi:isolate'},i.signer)):null,
 i.time?el('small',{class:'muted'},' (נבדק ב-'+new Date(i.time).toLocaleTimeString('he-IL')+')'):null,
 i.required?el('div',{class:i.blocked?'bad':'ok',style:'margin-top:4px'},i.blocked?'האינטרנט במחשב חסום כרגע.':'האינטרנט פתוח.'):null,
 i.status==='NoNetwork'&&i.detail?el('small',{class:'muted',style:'display:block'},i.detail):null)}
function renderIsp(i){isp=i;$('ispStatus').replaceChildren(ispLine(i));$('ispTrust').hidden=!(i.chain&&i.chain.length)||i.status==='Filtered';
 if(!$('ispConfirm').hidden)renderIspNow(i)}
function renderIspNow(i){const n=$('ispNow');n.replaceChildren(ispLine(i));
 if(i.status!=='Filtered')n.append(el('div',{class:'bad',style:'margin-top:4px'},'שימו לב: כרגע הסינון לא מזוהה, ולכן ההפעלה תחסום את האינטרנט מיד. אם אתם כן מחוברים דרך הסינון, לחצו קודם "זה הסינון שלי" בחלק "אינטרנט רק דרך הסינון" למטה.'))}
function loadBoot(){return api('/api/boot',{},false).then(b=>{const d=$('bootStatus');d.hidden=!b.lockdown;if(!b.lockdown)return;
 d.className='machine '+(b.winReOff===false?'warn':'tip');
 d.replaceChildren(el('b',{},'מצב חסימת מצב בטוח וסביבת השחזור: '),
  b.winReOff===true?el('span',{class:'ok'},'סביבת השחזור כבויה ✓'):b.winReOff===false?el('span',{class:'bad'},'ווינדוס לא איפשר לכבות את סביבת השחזור. '+(b.error||'')):el('span',{class:'muted'},'בודק…'),
  b.safeModePending?el('div',{class:'bad'},'יש בקשה להפעלה מחדש במצב בטוח, והיא תבוטל.'):'',
  el('div',{class:'muted',style:'font-size:12px'},'בדיקה לאחרונה: '+(b.checked?new Date(b.checked).toLocaleTimeString('he-IL'):'עדיין לא')+'. כדי לוודא: הגדרות > מערכת > שחזור > "הפעל מחדש עכשיו" אמור להציג הודעה שסביבת השחזור לא נמצאה.'))}).catch(()=>{})}
function loadIsp(){loadBoot();return api('/api/isp',{},false).then(renderIsp).catch(()=>{})}
function checkIsp(){$('ispStatus').replaceChildren(el('span',{class:'muted'},'בודק…'));return post('/api/isp/check',{}).then(renderIsp).catch(fail)}
$('ispCheck').onclick=checkIsp;
[['ispNetfree','isp.netfree'],['ispEtrog','isp.etrog']].forEach(([id,k])=>$(id).onchange=e=>post('/api/settings',{key:k,value:String(e.target.checked)}).then(st=>{render(st);loadIsp();toast('הספקים עודכנו')}).catch(x=>{fail(x);e.target.checked=!e.target.checked}));
function renderProviders(st){$('ispNetfree').checked=st.settings['isp.netfree'];$('ispEtrog').checked=st.settings['isp.etrog'];
 $('ispNoProvider').hidden=st.settings['isp.netfree']||st.settings['isp.etrog']||(st.lists.ispNames||[]).length>0||(st.lists.ispThumbprints||[]).length>0}
$('msUpdates').onclick=()=>post('/api/network/msupdates',{}).then(st=>{render(st);toast('עדכוני ווינדוס ואופיס הותרו')}).catch(fail);
$('pwf').onsubmit=e=>{e.preventDefault();if($('pwNew').value!==$('pwNew2').value){toast('שתי הסיסמאות החדשות לא זהות',true);return}
 post('/api/password',{which:$('pwWhich').value,current:$('pwCur').value,next:$('pwNew').value}).then(()=>{['pwCur','pwNew','pwNew2'].forEach(i=>$(i).value='');toast('הסיסמה שונתה. שמרו אותה במקום בטוח.')}).catch(fail)};
$('ispTrust').onclick=()=>$('ispTrustNote').hidden=false;
$('ispTrustCancel').onclick=()=>$('ispTrustNote').hidden=true;
$('ispTrustGo').onclick=()=>post('/api/isp/trust',{}).then(i=>{$('ispTrustNote').hidden=true;renderIsp(i);load();toast('התעודה נוספה לתעודות שסומכים עליהן')}).catch(fail);
function askIsp(){$('ispConfirm').hidden=false;$('ispAgree').checked=false;$('ispGo').disabled=true;$('ispNow').textContent='בודק את החיבור…';$('ispConfirm').scrollIntoView({behavior:'smooth',block:'center'});
 post('/api/isp/check',{}).then(i=>{renderIsp(i);renderIspNow(i)}).catch(fail)}
$('ispAgree').onchange=()=>$('ispGo').disabled=!$('ispAgree').checked;
$('ispCancel').onclick=()=>$('ispConfirm').hidden=true;
$('ispGo').onclick=()=>post('/api/settings',{key:'isp.required',value:'true'}).then(st=>{$('ispConfirm').hidden=true;render(st);loadIsp();toast('"אינטרנט רק דרך הסינון" הופעל')}).catch(fail);
setInterval(()=>{if(!$('app').hidden&&!document.querySelector('[data-page=features]').hidden)loadIsp()},30000);
const ASK={'boot.lockdown':askBoot,'isp.required':askIsp};
function toolsUnlock(){const m=el('select',{},...[15,30,60,120].map(x=>el('option',{value:x},x<60?x+' דקות':(x/60===1?'שעה':'שעתיים'))));
 return el('div',{class:'row',style:'margin-top:6px'},el('small',{},'שחרור זמני'+(scope?' עבור '+scope:'')+':'),m,
  el('button',{class:'ghost',onclick:()=>post('/api/tools/unlock',{minutes:m.value}).then(r=>toast('כלי המנהל משוחררים עד '+new Date(r.until).toLocaleTimeString('he-IL',{hour:'2-digit',minute:'2-digit'}))).catch(fail)},'לשחרר'),
  el('button',{class:'link',onclick:()=>post('/api/tools/unlock',{minutes:'0'}).then(()=>toast('כלי המנהל חסומים שוב')).catch(fail)},'לחסום שוב עכשיו'))}
function renderToggles(s){$('toggles').replaceChildren(...TOGGLES.filter(([k])=>!scope||PER_USER.includes(k)).map(([k,t,d,w])=>{const txt=el('div',{},el('b',{},t),el('small',{},d));
 if(k==='apps.blockAdminTools'&&s[k])txt.append(toolsUnlock());
 txt.append(el('small',{class:'badge',style:'display:inline-block;margin-top:4px'},PER_USER.includes(k)?(scope?'חל רק על '+scope:'ברירת מחדל: חל על משתמשים בלי הגדרות נפרדות'):'חל על כל המחשב'));if(w&&!s[k])txt.append(el('div',{class:'warn'},w));
 return el('div',{class:'toggle'},txt,sw(s[k],i=>(ASK[k]&&i.checked)?(i.checked=false,ASK[k]()):post('/api/settings',{key:k,value:String(i.checked)}).then(st=>{render(st);if(k==='isp.required')loadIsp();if(k==='boot.lockdown')loadBoot();if(k==='wifi.knownOnly')loadWifi();toast(t+(i.checked?' – הודלק':' – כובה'))}).catch(x=>{fail(x);i.checked=!i.checked}),t))}))}

// Wi-Fi networks
function loadWifi(){api('/api/wifi',{},false).then(w=>{
 $('wifiAllowed').replaceChildren(...(w.allowed.length?w.allowed.map(n=>el('li',{},el('span',{},n),el('button',{class:'link',onclick:()=>post('/api/wifi/list',{action:'remove',value:n}).then(()=>{toast('הרשת '+n+' הוסרה מהרשימה');loadWifi()}).catch(fail)},'הסרה'))):[el('li',{class:'muted'},w.on?'הרשימה ריקה':'הרשימה תתמלא ברשתות השמורות במחשב כשתדליקו את היכולת')]));
 const extra=(w.saved||[]).filter(n=>!w.allowed.includes(n));
 $('wifiSaved').replaceChildren(...(extra.length?extra.map(n=>el('option',{value:n},n)):[el('option',{value:''},'אין רשתות שמורות נוספות')]));$('wifiAdd').disabled=!extra.length;
 $('wifiNote').textContent=!w.hasWifi?'לא נמצא במחשב כרטיס Wi-Fi.':'כדי להוסיף רשת חדשה: מכבים את היכולת, מתחברים לרשת, ומדליקים שוב. הרשת תתווסף לרשימה אוטומטית.'}).catch(()=>{})}
$('wifiAdd').onclick=()=>{const n=$('wifiSaved').value;if(n)post('/api/wifi/list',{action:'add',value:n}).then(()=>{toast('הרשת '+n+' נוספה');loadWifi()}).catch(fail)};

// Lists
const APP_LISTS=['allowApps','blockApps'];
function renderList(id,items){$(id).replaceChildren(...(items.length?items.map(v=>el('li',{},el('span',{},v),el('button',{class:'link',onclick:()=>edit(id,'remove',v).then(()=>toast('הוסר: '+v))},'הסרה'))):[el('li',{class:'muted'},'הרשימה ריקה')]))}
function edit(list,action,value){const url=APP_LISTS.includes(list)?'/api/apps/list':'/api/network/list';return post(url,{list,action,value}).then(render).catch(x=>{fail(x);throw x})}
document.querySelectorAll('form.add').forEach(f=>f.onsubmit=e=>{e.preventDefault();const i=f.querySelector('input');const v=i.value.trim();edit(f.dataset.list,'add',v).then(()=>{i.value='';toast('נוסף: '+v)}).catch(()=>{})});
$('mode').onchange=e=>post('/api/network/mode',{mode:e.target.value}).then(st=>{render(st);toast(e.target.value==='Allowlist'?'עכשיו רק האתרים ברשימה מותרים':'עכשיו רק האתרים ברשימה חסומים')}).catch(fail);
function renderMode(st){const allow=st.mode==='Allowlist';$('mode').value=st.mode;
 $('modeHint').textContent=allow?'רשימה לבנה: כל האתרים חסומים, ורק מה שתכתבו ברשימה מותר. מתאים כשרוצים לאפשר מעט מאוד אתרים.':'רשימה שחורה: כל האתרים פתוחים, ורק מה שתכתבו ברשימה ייחסם. מתאים כשרוצים לחסום כמה אתרים ספציפיים.';
 $('domainsTitle').textContent=allow?'אתרים מותרים':'אתרים חסומים';
 $('excHint').textContent=allow?'כתובות שייחסמו למרות שהאתר שלהן ברשימת המותרים. למשל: youtube.com מותר, אבל youtube.com/shorts חסום.':'כתובות שיהיו פתוחות למרות שהאתר שלהן חסום. למשל: youtube.com חסום, אבל סרטון מסוים מותר.';
 $('netOff').hidden=st.settings['network.enabled'];$('urlOff').hidden=!st.settings['network.enabled']||st.settings['network.urlFiltering']}
function renderScope(st){const bar=$('scopebar');document.body.dataset.ctx=st.scope?'user':'computer';
 bar.className='scopebar'+(st.scope?'':' general');$('usersTab').textContent=st.scope?'שעות':'משתמשים';
 $('scopeText').replaceChildren(...(st.scope?['מנהלים עכשיו את ',el('b',{},st.scope),'. כל שינוי בתוכנות, באתרים ובשעות חל רק עליו.']:[el('b',{},'המחשב כולו'),'. הגדרות שחלות על כל המחשב, וברירת המחדל למשתמשים בלי הגדרות נפרדות.']));
 $('scopeUrlWarn').hidden=!(st.scopes.length&&!st.settings['network.urlFiltering'])}
$('backHome').onclick=()=>openTab('home');
function setScope(u,tab){scope=u||'';catalog=null;return api(q('/api/state')).then(st=>{render(st);openTab(tab||'features')}).catch(fail)}

// Dashboard
const stLine=(on,text)=>el('div',{class:'st '+(on?'on':'off')},el('span',{class:'i'},on?'✓':'–'),text);
function scopeLines(m){return[
 stLine(m.appsEnabled,m.appsEnabled?'נעילת תוכנות פועלת'+(m.lockedApps?' ('+m.lockedApps+' נעולות)':'')+(m.timedApps?', '+m.timedApps+' לפי שעות':''):'נעילת תוכנות כבויה'),
 stLine(m.defaultDeny,m.defaultDeny?'תוכנות לא מוכרות נחסמות':'תוכנות לא מוכרות מותרות'),
 stLine(m.sitesOn,!m.sitesOn?'סינון אתרים כבוי':m.allowlist?'רשימה לבנה: '+m.sites+' אתרים מותרים':m.sites?m.sites+' אתרים חסומים':'אין אתרים חסומים'),
 stLine(m.adminTools,m.adminTools?'כלי מנהל חסומים':'כלי מנהל פתוחים'),
 stLine(m.noInternetApps>0,m.noInternetApps?m.noInternetApps+' תוכנות בלי אינטרנט':'אין תוכנות בלי אינטרנט')]}
const ISPDASH={Filtered:'מחובר דרך הסינון',NotFiltered:'לא מחובר דרך הסינון',NoNetwork:'אין רשת',Unknown:'עוד לא נבדק'};
function renderDash(d){const c=d.computer;
 const pc=el('button',{class:'card pc'},el('h3',{},'המחשב כולו'),
  stLine(c.network,c.network?'סינון אתרים וחומת אש פועלים':'סינון אתרים וחומת אש כבויים'),
  stLine(c.urlFiltering,c.urlFiltering?'סינון לפי כתובת מלאה':'סינון לפי כתובת מלאה כבוי'),
  stLine(c.isp.required,c.isp.required?'אינטרנט רק דרך הסינון: '+(c.isp.blocked?'האינטרנט חסום עכשיו':ISPDASH[c.isp.status]||''):'אינטרנט רק דרך הסינון כבוי'),
  stLine(c.clock,c.clock?'השעון נעול':'השעון לא נעול'),
  stLine(c.vpn,c.vpn?'VPN ופרוקסי חסומים':'VPN ופרוקסי לא חסומים'),
  stLine(c.wifi,c.wifi?'רק רשתות Wi-Fi מוכרות':'אפשר להתחבר לכל רשת Wi-Fi'),
  stLine(c.boot,c.boot?'מצב בטוח וסביבת השחזור חסומים':'מצב בטוח וסביבת השחזור פתוחים'),
  (c.otherFirewalls||[]).length?el('div',{class:'warn'},'חומת אש של '+c.otherFirewalls.join(', ')+' פועלת במחשב. חלק מהחסימות של Guardian לא יעבדו (פירוט בלשונית "יכולות").'):'',
  el('small',{class:'muted',style:'margin-top:4px'},'ברירת מחדל למשתמשים בלי הגדרות נפרדות:'),...scopeLines(d.general),
  el('span',{class:'go'},'לניהול המחשב כולו ←'));
 pc.onclick=()=>setScope('');
 const users=d.users.map(u=>{const badges=[u.online?el('span',{class:'badge ok'},'מחובר עכשיו'):null,u.lockedNow?el('span',{class:'badge bad'},'בשעות נעילה'):null,
   u.unlockedUntil?el('span',{class:'badge'},'שוחרר עד '+new Date(u.unlockedUntil).toLocaleTimeString('he-IL',{hour:'2-digit',minute:'2-digit'})):null];
  const b=el('button',{class:'card'},el('h3',{},u.name,...badges),
   el('small',{class:'muted'},u.own?'הגדרות נפרדות':'עדיין לפי ברירת המחדל'),...scopeLines(u.summary),
   stLine(u.summary.schedule,u.summary.schedule?'יש שעות נעילה':'אין שעות נעילה'),
   stLine(u.summary.breakEvery>0,u.summary.breakEvery>0?'מניעת התמכרות: הפסקה כל '+u.summary.breakEvery+' דקות':'בלי מניעת התמכרות'),
   el('span',{class:'go'},'לניהול של '+u.name+' ←'));
  b.onclick=()=>enterUser(u);return b});
 $('cards').replaceChildren(...users,pc)}
function loadDash(){return api('/api/dashboard',{},false).then(d=>{renderDash(d);const f=d.computer.otherFirewalls||[];$('fwWarn').hidden=!f.length;$('fwWarn').textContent=f.length?'נמצאה במחשב חומת אש פעילה של: '+f.join(', ')+'.':''}).catch(()=>{})}
// Entering a user gives them settings of their own (a copy of the defaults) the first time.
function enterUser(u){(u.own?Promise.resolve():post('/api/users/own',{name:u.name,own:'true'}).then(()=>toast(u.name+' קיבל הגדרות נפרדות, שמתחילות כהעתק של ברירת המחדל.')))
 .then(()=>setScope(u.name)).catch(fail)}
setInterval(()=>{if(!$('app').hidden&&!document.querySelector('[data-page=home]').hidden)loadDash()},15000);
function render(st){state=st;scope=st.scope||'';renderScope(st);renderProviders(st);idleMin=st.idleMinutes||5;renderToggles(st.settings);renderMode(st);for(const[k,v]of Object.entries(st.lists))if($(k))renderList(k,v);
 $('idle').replaceChildren(...st.idleChoices.map(m=>el('option',{value:m,selected:m===idleMin},String(m))));if(catalog)renderCatalog()}
function load(){api(q('/api/state')).then(render).catch(fail)}
$('idle').onchange=e=>post('/api/settings/idle',{minutes:e.target.value}).then(st=>{render(st);toast('הממשק יינעל אחרי '+e.target.value+' דקות ללא פעילות')}).catch(fail);

// Programs (catalog)
let catalog=null;const SRCN={StartMenu:'תפריט התחל',InstalledApps:'אפליקציות מותקנות',Store:'Microsoft Store'};
function loadCatalog(refresh){$('catNote').textContent='טוען את רשימת התוכנות… (בפעם הראשונה זה יכול לקחת כחצי דקה)';api(q('/api/catalog',refresh?'&refresh=1':'')).then(c=>{catalog=c;renderCatalog()}).catch(x=>{$('catNote').textContent='';fail(x)})}
$('refresh').onclick=()=>loadCatalog(true);$('q').oninput=()=>renderCatalog();$('onlySet').onchange=()=>renderCatalog();
function setApp(a,kind,i){post('/api/catalog/set',{path:a.exePath,kind,value:String(i.checked)}).then(c=>{catalog=c;load();
 const msg={lock:['ננעלה','שוחררה מנעילה'],internet:['לא תוכל להתחבר לאינטרנט','יכולה שוב להתחבר לאינטרנט'],direct:['קיבלה גישה ישירה לאינטרנט','תעבור שוב דרך הסינון']}[kind];toast(a.name+' '+(i.checked?msg[0]:msg[1])+(kind==='direct'?'':(scope?' (עבור '+scope+')':'')))}).catch(x=>{fail(x);i.checked=!i.checked})}
function renderCatalog(){if(!catalog)return;const q=$('q').value.trim().toLowerCase(),only=$('onlySet').checked,direct=state&&state.settings['network.urlFiltering'];
 const rows=catalog.filter(a=>(!q||a.name.toLowerCase().includes(q)||a.exePath.toLowerCase().includes(q))&&(!only||a.locked||a.internetBlocked||a.directInternet||(a.hours||[]).length));
 $('catNote').textContent=catalog.length+' תוכנות נמצאו'+(q||only?', מוצגות '+rows.length:'')+'.'+(state&&!state.settings['apps.enabled']&&catalog.some(a=>a.locked)?' שימו לב: "נעילת תוכנות" כבויה בלשונית "יכולות", ולכן הנעילות לא נאכפות.':'');
 $('catalog').replaceChildren(...rows.map(a=>el('tr',{},el('td',{},el('b',{},a.name),el('div',{class:'hint ltr',style:'margin:0;font-size:11px'},a.exePath)),el('td',{},el('span',{class:'badge'},SRCN[a.source]||a.source)),
  el('td',{},sw(a.locked,i=>setApp(a,'lock',i),'נעולה')),el('td',{},sw(a.internetBlocked,i=>setApp(a,'internet',i),'בלי אינטרנט')),el('td',{},sw(a.directInternet,i=>setApp(a,'direct',i),'גישה ישירה',!direct&&!a.directInternet)),
  el('td',{},el('button',{class:'link',onclick:e=>appHours(a,e.target.closest('tr'))},(a.hours||[]).length?'חסומה לפי שעות':'+ שעות')))))}
// Blocked hours for one program, edited in a row under it.
function appHours(a,tr){const open=tr.nextElementSibling;if(open&&open.classList.contains('hrs')){open.remove();return}
 const box=el('div');(a.hours||[]).forEach(w=>box.append(winRow(w)));
 const save=()=>post('/api/catalog/hours',{path:a.exePath,windows:JSON.stringify(readWins(box))}).then(c=>{catalog=c;load();renderCatalog();
  toast(readWins(box).length?a.name+' תיחסם בשעות שנקבעו'+(scope?' (עבור '+scope+')':''):'הוסרו שעות החסימה של '+a.name)}).catch(fail);
 tr.after(el('tr',{class:'hrs'},el('td',{colspan:6},el('div',{class:'hint',style:'margin:0'},'השעות שבהן '+a.name+' חסומה'+(scope?' עבור '+scope:'')+'. בשאר הזמן היא עובדת כרגיל (אלא אם היא מסומנת "נעולה").'),box,
  el('div',{class:'row',style:'margin-top:8px'},el('button',{class:'ghost',onclick:()=>box.append(winRow({days:DAYS.map(d=>d[0]),start:'22:00',end:'07:00'}))},'+ טווח שעות'),el('button',{onclick:save},'שמירה')))))}

// Direct-connection attempts
function loadDirect(){api('/api/direct',{},false).then(rows=>{$('direct').replaceChildren(...rows.map(d=>el('tr',{},el('td',{class:'l'},d.appPath),el('td',{},String(d.count)),el('td',{},new Date(d.last).toLocaleString('he-IL')),
  el('td',{},d.allowed?el('span',{class:'ok'},'מאושרת'):el('button',{class:'link',onclick:()=>edit('directApps','add',d.appPath).then(()=>{toast('התוכנה קיבלה גישה ישירה. אם היא עדיין לא מתחברת, סגרו ופתחו אותה מחדש.');loadDirect()}).catch(()=>{})},'לאפשר גישה ישירה')))));
 $('directEmpty').textContent=rows.length?'':(state&&state.settings['network.urlFiltering']?'עדיין לא נחסמו תוכנות. מצוין.':'"סינון לפי כתובת מלאה" כבוי, ולכן אין מה להציג.')}).catch(()=>{})}

// Users
const DAYS=[['Sunday','א'],['Monday','ב'],['Tuesday','ג'],['Wednesday','ד'],['Thursday','ה'],['Friday','ו'],['Saturday','ש']];
function winRow(w){const d=el('span',{class:'days'},...DAYS.map(([v,l])=>el('label',{},el('input',{type:'checkbox',value:v,checked:w.days.includes(v)}),l)));
 const r=el('div',{class:'win'},d,'מ-',el('input',{type:'time',value:w.start,class:'s'}),'עד',el('input',{type:'time',value:w.end,class:'e'}),el('button',{class:'link',onclick:()=>r.remove()},'מחיקה'));return r}
function readWins(box){return[...box.querySelectorAll('.win')].map(r=>({days:[...r.querySelectorAll('.days input:checked')].map(i=>i.value),start:r.querySelector('.s').value,end:r.querySelector('.e').value}))}
function ownRow(a){const r=el('div',{class:'row',style:'margin-top:8px'});
 if(a.ownRules){const back=el('button',{class:'link'},'למחוק את ההגדרות הנפרדות ולחזור לברירת המחדל');let armed=false;
  back.onclick=()=>{if(!armed){armed=true;back.textContent='בטוח? ההגדרות הנפרדות של '+a.name+' יימחקו. לחץ שוב לאישור';return}
   post('/api/users/own',{name:a.name,own:'false'}).then(st=>{const mine=scope.toLowerCase()===a.name.toLowerCase();if(mine)scope='';load();if(mine)openTab('home');else loadUsers();toast(a.name+' חזר לברירת המחדל')}).catch(fail)};
  r.append(...[el('span',{class:'badge'},'תוכנות ואתרים: הגדרות נפרדות'),scope?null:el('button',{class:'ghost',onclick:()=>setScope(a.name,'programs')},'לניהול של '+a.name),back].filter(Boolean))}
 else r.append(el('span',{class:'badge'},'תוכנות ואתרים: לפי ברירת המחדל'),el('button',{class:'ghost',onclick:()=>enterUser({name:a.name,own:false})},'לניהול של '+a.name));
 return r}
function loadUsers(){api('/api/users').then(u=>{$('allLocked').hidden=!u.everyoneLocked;$('users').replaceChildren(...u.accounts.filter(a=>!scope||a.name.toLowerCase()===scope.toLowerCase()).map(a=>{
 const wins=a.rule?a.rule.lockedDuring.windows:[];const box=el('div');wins.forEach(w=>box.append(winRow(w)));
 const warn=el('input',{type:'number',min:0,max:60,value:a.rule?a.rule.warningMinutes:5,style:'width:70px'});
 const BR=[0,5,10,15,20,25,30,40,45,50,60];const cur=a.rule?a.rule.breakEveryMinutes||0:0;if(!BR.includes(cur))BR.push(cur);
 const brk=el('select',{},...BR.sort((x,y)=>x-y).map(m=>el('option',{value:m,selected:m===cur},m?(m<60||m%60?m+' דקות':(m/60===1?'שעה':m/60+' שעות')):'כבוי')));
 const status=a.unlockedUntil?el('span',{class:'badge ok'},'שוחרר זמנית עד '+new Date(a.unlockedUntil).toLocaleTimeString('he-IL',{hour:'2-digit',minute:'2-digit'})):wins.length?el('span',{class:'badge'},'יש שעות נעילה'):cur?el('span',{class:'badge'},'הפסקה כל '+cur+' דקות'):el('span',{class:'badge'},'ללא הגבלה');
 const save=()=>post('/api/users/set',{user:a.name,windows:JSON.stringify(readWins(box)),warning:warn.value,breakEvery:brk.value}).then(()=>{toast(readWins(box).length||+brk.value?'ההגדרות של '+a.name+' נשמרו':'הנעילה של '+a.name+' בוטלה');load();loadUsers()}).catch(fail);
 const mins=el('select',{},...[15,30,60,120,240].map(m=>el('option',{value:m},m<60?m+' דקות':(m/60)+' שעות')));
 const unlock=el('div',{class:'row',style:'margin-top:8px'},el('span',{class:'muted'},'שחרור זמני:'),mins,
  el('button',{class:'ghost',onclick:()=>post('/api/users/unlock',{user:a.name,minutes:mins.value}).then(()=>{toast(a.name+' יכול להשתמש במחשב גם בשעות הנעילה, למשך '+mins.selectedOptions[0].textContent);loadUsers()}).catch(fail)},'לשחרר'),
  a.unlockedUntil?el('button',{class:'link',onclick:()=>post('/api/users/unlock',{user:a.name,minutes:'0'}).then(()=>{toast('השחרור הזמני בוטל');loadUsers()}).catch(fail)},'ביטול השחרור'):null);
 return el('div',{class:'user'},el('div',{class:'row'},el('b',{},a.name),status),
  ownRow(a),
  el('div',{class:'row',style:'margin-top:8px'},el('span',{},'מניעת התמכרות: יציאה אחרי'),brk,el('span',{class:'muted'},'של שימוש רציף')),
  el('div',{class:'hint',style:'margin:6px 0 0'},'שעות שבהן '+a.name+' לא יכול להיות מחובר:'),box,
  el('div',{class:'row',style:'margin-top:8px'},el('button',{class:'ghost',onclick:()=>box.append(winRow({days:DAYS.slice(0,5).map(d=>d[0]),start:'23:00',end:'07:00'}))},'+ טווח שעות'),
   el('label',{},'התראה ',warn,' דקות לפני הניתוק'),el('button',{onclick:save},'שמירה')),wins.length?unlock:null)}))}).catch(fail)}

// Activity
const SRC={web:'אתר',dns:'אתר',app:'תוכנה',direct:'חיבור ישיר',boot:'אתחול',isp:'סינון',wifi:'Wi-Fi'};
const hostOf=t=>{try{return new URL(/^[a-z]+:\/\//i.test(t)?t:'http://'+t).hostname}catch{return t}};
const isFullUrl=t=>/^[a-z]+:\/\/[^/]+\/./i.test(t)||(/^[^:/]+\/./.test(t));
// A row is changed in the rules of the account it came from (its own rules, or the general ones).
const rowScope=r=>{const u=short(r.user);return state&&u&&state.scopes.some(s=>s.toLowerCase()===u.toLowerCase())?u:''};
const forWhom=u=>u?' (עבור '+u+')':(state&&state.scopes.length?' (בהגדרות הכלליות)':'');
function site(target,allow,whole,u){return post('/api/site',{target,allow:String(allow),whole:String(whole),user:u}).then(()=>{load();
 toast((whole?'האתר '+hostOf(target):'הכתובת')+(allow?' הותר':' נחסם')+(whole?'':'ה')+forWhom(u)+'. השינוי ייכנס לתוקף בטעינה הבאה של הדף.')}).catch(fail)}
function appAllow(path,kind,u){return post('/api/apps/allow',{path,scope:kind,user:u}).then(r=>{load();toast((kind==='folder'?'כל התוכנות בתיקייה הותרו: ':'התוכנה הותרה: ')+r.allowed+forWhom(u)+'. אפשר לפתוח אותה שוב.')}).catch(fail)}
const btn=(t,f)=>el('button',{class:'link',onclick:f},t);
function actions(r){const t=r.target,u=rowScope(r);
 if(r.source==='web'||r.source==='dns'){const b=[];const h=hostOf(t);
  if(isFullUrl(t))b.push(r.allowed?btn('לחסום כתובת זו',()=>site(t,false,false,u)):btn('להתיר כתובת זו',()=>site(t,true,false,u)));
  b.push(r.allowed?btn('לחסום את כל '+h,()=>site(t,false,true,u)):btn('להתיר את כל '+h,()=>site(t,true,true,u)));return b}
 if(r.source==='app'&&!r.allowed)return[btn('להתיר תוכנה זו',()=>appAllow(t,'file',u)),btn('להתיר את כל התיקייה',()=>appAllow(t,'folder',u))];
 if(r.source==='direct')return[btn('לאפשר גישה ישירה',()=>edit('directApps','add',t).then(()=>toast('התוכנה קיבלה גישה ישירה')).catch(()=>{}))];
 return[]}
let timer;function tick(){clearTimeout(timer);if($('app').hidden)return;if($('pause').checked||document.querySelector('[data-page=log]').hidden){timer=setTimeout(tick,3000);return}
 api('/api/log',{},false).then(rows=>{const ob=$('onlyBlocked').checked,q=$('filter').value.toLowerCase();
 $('log').replaceChildren(...rows.filter(r=>(!scope||short(r.user).toLowerCase()===scope.toLowerCase())&&(!ob||!r.allowed)&&(!q||r.target.toLowerCase().includes(q))).slice(0,200).map(r=>el('tr',{},
  el('td',{},new Date(r.time).toLocaleTimeString('he-IL')),el('td',{},short(r.user)||(r.source==='dns'?'(לא ידוע)':'')),el('td',{},SRC[r.source]||r.source),el('td',{class:r.allowed?'ok':'bad'},r.allowed?'הותר':'נחסם'),
  el('td',{class:'l'},r.target),el('td',{},el('div',{class:'acts'},...actions(r))))))}).catch(()=>{}).finally(()=>timer=setTimeout(tick,3000))}
$('pause').onchange=()=>tick();

// Settings
// Delay before turning off
const PLAB={'/api/settings':'הגדרה','/api/catalog/set':'תוכנה','/api/catalog/hours':'שעות חסימה לתוכנה','/api/apps/list':'רשימת תוכנות','/api/apps/allow':'היתר לתוכנה',
 '/api/site':'היתר לאתר','/api/network/list':'רשימת רשת','/api/network/mode':'מצב סינון האתרים','/api/users/set':'שעות ומניעת התמכרות','/api/users/unlock':'שחרור זמני של משתמש',
 '/api/tools/unlock':'שחרור זמני של כלי מנהל','/api/users/own':'חזרה לברירת המחדל','/api/wifi/list':'הוספת רשת Wi-Fi','/api/network/msupdates':'החרגת עדכוני מיקרוסופט','/api/isp/trust':'תעודת סינון','/api/cooling/set':'הקטנת ההשהיה'};
function describePending(p){const b=p.body||{};const t=(TOGGLES.find(x=>x[0]===b.key)||[])[1];
 const what=p.path==='/api/settings'?(t||b.key)+' – '+(b.value==='true'?'הדלקה':'כיבוי'):p.path==='/api/cooling/set'?'הקטנת ההשהיה ל-'+b.hours+' שעות':
  (PLAB[p.path]||p.path)+(b.path?': '+b.path.split('\\').pop():b.value?': '+b.value:b.target?': '+b.target:b.minutes?': '+b.minutes+' דקות':'');
 return what+(p.user?' (עבור '+p.user+')':'')}
function loadCooling(){api('/api/cooling',{},false).then(c=>{$('coolHours').value=c.hours;
 $('coolPending').replaceChildren(...(c.pending.length?c.pending.map(p=>el('li',{},el('span',{},describePending(p)+' · יתבצע ב-'+fmtDue(p.due)),
  el('button',{class:'link',onclick:()=>post('/api/cooling/cancel',{id:p.id}).then(()=>{toast('השינוי בוטל');loadCooling()}).catch(fail)},'ביטול'))):[el('li',{class:'muted'},'אין שינויים שממתינים')]));
 const u=$('coolUninstall');u.replaceChildren();if(!c.hours)return;
 if(!c.uninstallDue)u.append(el('span',{class:'muted'},'הסרת Guardian:'),el('button',{class:'ghost',onclick:()=>post('/api/cooling/uninstall',{action:'request'}).then(r=>{toast('ההסרה תתאפשר ב-'+fmtDue(r.due));loadCooling()}).catch(fail)},'בקשת הסרה'));
 else u.append(el('span',{class:c.uninstallReady?'badge ok':'badge'},c.uninstallReady?'אפשר להסיר עכשיו':'ההסרה תתאפשר ב-'+fmtDue(c.uninstallDue)),
  el('button',{class:'link',onclick:()=>post('/api/cooling/uninstall',{action:'cancel'}).then(()=>{toast('בקשת ההסרה בוטלה');loadCooling()}).catch(fail)},'ביטול הבקשה'))}).catch(()=>{})}
$('coolSave').onclick=()=>post('/api/cooling/set',{hours:$('coolHours').value}).then(()=>{toast('ההשהיה נשמרה: '+$('coolHours').value+' שעות');loadCooling()}).catch(fail);
function loadHealth(){api('/api/health').then(h=>{$('health').replaceChildren(
 h.dnsLastSuccess?el('div',{class:'ok'},'בירור כתובות עובד. הצלחה אחרונה: '+new Date(h.dnsLastSuccess).toLocaleTimeString('he-IL')):el('div',{class:'muted'},'עדיין לא היו בירורי כתובות (סינון האתרים כבוי או שהשירות רק התחיל).'),
 h.dnsLastError?el('div',{class:'bad'},'שגיאה אחרונה: '+h.dnsLastError):'',
 h.clockSynced?el('div',{class:'ok'},'השעה אומתה מול האינטרנט ב-'+new Date(h.clockLastSync).toLocaleTimeString('he-IL')+'. השעה הנכונה לפי Guardian: '+new Date(h.clockTrusted).toLocaleTimeString('he-IL')):el('div',{class:'muted'},'השעה עדיין לא אומתה מול האינטרנט.'))}).catch(fail)}
$('healthBtn').onclick=loadHealth;
$('uninstall').onclick=()=>{if(window.chrome&&chrome.webview){chrome.webview.postMessage('uninstall')}else{$('uninstallNote').hidden=false}};

api(q('/api/state'),{},false).then(st=>{show(true);render(st)}).catch(()=>show(false));
</script>
</body></html>
""";
}
