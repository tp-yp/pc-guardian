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
:root{--bg:#f6f7f9;--card:#fff;--text:#1d2330;--muted:#5f6876;--ok:#15803d;--bad:#b91c1c;--accent:#2563eb;--line:#e5e7eb;--warn:#92400e;--warnbg:#fffbeb;--soft:#eef2ff}
@media (prefers-color-scheme:dark){:root{--bg:#111418;--card:#1b2027;--text:#e6e9ee;--muted:#9aa3af;--ok:#4ade80;--bad:#f87171;--accent:#60a5fa;--line:#2b313a;--warn:#fcd34d;--warnbg:#2a2210;--soft:#1e2636}}
*{box-sizing:border-box}body{margin:0;font-family:system-ui,"Segoe UI",Arial,sans-serif;background:var(--bg);color:var(--text);font-size:14px}
[hidden]{display:none !important}
header{padding:12px 20px;display:flex;justify-content:space-between;align-items:center;border-bottom:1px solid var(--line);gap:12px;flex-wrap:wrap;background:var(--card);position:sticky;top:0;z-index:5}
h1{font-size:18px;margin:0}
nav{display:flex;gap:4px;flex-wrap:wrap}
nav button,.sub button{background:none;color:var(--text);border:1px solid transparent}
nav button.on,.sub button.on{border-color:var(--line);background:var(--soft);font-weight:600}
.sub{display:flex;gap:6px;margin-bottom:12px;flex-wrap:wrap}
main{max-width:1100px;margin:auto;padding:16px;display:grid;gap:16px;grid-template-columns:1fr 1fr}
@media (max-width:820px){main{grid-template-columns:1fr}}
.page{display:contents}
section{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:16px;min-width:0}
.wide{grid-column:1/-1}
h2{font-size:16px;margin:0 0 6px}h3{font-size:14px;margin:14px 0 4px}
p.hint,.hint{color:var(--muted);font-size:13px;margin:0 0 10px;line-height:1.6}
input,select,button{font:inherit;padding:7px 10px;border-radius:7px;border:1px solid var(--line);background:var(--card);color:var(--text)}
button{background:var(--accent);color:#fff;border:0;cursor:pointer}
button.link{background:none;color:var(--accent);padding:2px 6px}
button.ghost{background:none;color:var(--text);border:1px solid var(--line)}
ul{list-style:none;padding:0;margin:8px 0 0;max-height:260px;overflow:auto}
li{display:flex;justify-content:space-between;gap:8px;padding:6px 0;border-bottom:1px solid var(--line);align-items:center}
li span,.ltr{direction:ltr;word-break:break-all;unicode-bidi:plaintext}
table{width:100%;border-collapse:collapse;font-size:13px}th{text-align:right;color:var(--muted);font-weight:500;padding:6px;border-bottom:1px solid var(--line)}
td{padding:6px;border-bottom:1px solid var(--line);vertical-align:middle}td.l{direction:ltr;text-align:left;word-break:break-all}
.ok{color:var(--ok)}.bad{color:var(--bad)}.muted{color:var(--muted)}
.row{display:flex;gap:8px;flex-wrap:wrap;align-items:center}
.toggle{display:flex;justify-content:space-between;gap:16px;padding:12px 0;border-bottom:1px solid var(--line)}.toggle:last-child{border:0}
.toggle b{display:block;margin-bottom:3px}.toggle small{color:var(--muted);line-height:1.6;display:block}
.switch{position:relative;width:44px;height:24px;flex:none;display:inline-block}.switch input{opacity:0;width:0;height:0}
.slider{position:absolute;inset:0;background:var(--line);border-radius:24px;transition:.2s;cursor:pointer}.slider:before{content:"";position:absolute;width:18px;height:18px;right:3px;top:3px;background:#fff;border-radius:50%;transition:.2s}
input:checked+.slider{background:var(--accent)}input:checked+.slider:before{transform:translateX(-20px)}
input:disabled+.slider{opacity:.4;cursor:not-allowed}
.warn{background:var(--warnbg);color:var(--warn);border-radius:8px;padding:8px 10px;font-size:13px;margin-top:6px;line-height:1.6}
.tip{background:var(--soft);border-radius:8px;padding:10px 12px;font-size:13px;line-height:1.7}
.tip ul{max-height:none;margin:4px 0 0}.tip li{display:list-item;list-style:disc;margin-right:18px;border:0;padding:1px 0}
.badge{font-size:12px;padding:2px 8px;border-radius:20px;background:var(--soft);white-space:nowrap}
.acts{display:flex;gap:2px;flex-wrap:wrap}
.scroll{max-height:60vh;overflow:auto}
.win{display:flex;gap:8px;flex-wrap:wrap;align-items:center;padding:8px;border:1px solid var(--line);border-radius:8px;margin-top:6px}
.days label{font-size:12px;display:inline-flex;align-items:center;gap:2px;margin-left:6px}
.user{border-bottom:1px solid var(--line);padding:12px 0}.user:last-child{border:0}
#toast{position:fixed;bottom:18px;left:50%;transform:translateX(-50%);background:#111;color:#fff;padding:10px 18px;border-radius:8px;font-size:14px;max-width:90vw;z-index:10;box-shadow:0 4px 14px #0004}
#toast.bad{background:#7f1d1d}
#login{max-width:380px;margin:15vh auto;padding:0 16px}
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
<button data-tab="features" class="on">יכולות</button><button data-tab="programs">תוכנות</button><button data-tab="net">רשת</button><button data-tab="users">משתמשים</button><button data-tab="log">פעילות</button><button data-tab="settings">הגדרות</button>
</nav>
<button class="ghost" id="logout">נעילה</button></header>

<main>
<!-- ===== יכולות ===== -->
<div class="page" data-page="features">
<section class="wide"><h2>מה פעיל עכשיו</h2>
<p class="hint">מומלץ להדליק יכולת אחת, לבדוק שהמחשב עובד כרגיל, ורק אז להמשיך לבאה. כל שינוי נכנס לתוקף תוך כמה שניות, וכיבוי מחזיר את המצב לקדמותו.</p>
<div id="toggles"></div></section>
</div>

<!-- ===== תוכנות ===== -->
<div class="page" data-page="programs" hidden>
<section class="wide"><h2>כל התוכנות במחשב</h2>
<p class="hint">הרשימה נאספת מתפריט התחל, מ"אפליקציות מותקנות" בהגדרות ומהחנות של Microsoft. לכל תוכנה אפשר לבחור:<br>
<b>נעולה</b> – התוכנה תיסגר מיד בכל פעם שמישהו ינסה לפתוח אותה, ויופיע מסך חסימה.<br>
<b>בלי אינטרנט</b> – התוכנה תיפתח כרגיל, אבל לא תוכל להתחבר לאינטרנט.<br>
<b>גישה ישירה</b> – רלוונטי רק כש"סינון לפי כתובת מלאה" דולק: התוכנה תתחבר לאינטרנט בלי לעבור בסינון. מתאים למשחקים, Steam, Spotify ותוכנות שמפסיקות לעבוד כשהסינון דולק.</p>
<div class="row"><input id="q" placeholder="חיפוש תוכנה…" style="flex:1;min-width:180px"><label><input type="checkbox" id="onlySet"> רק תוכנות עם הגבלה</label><button class="ghost" id="refresh">רענון הרשימה</button></div>
<div id="catNote" class="hint" style="margin-top:8px"></div>
<div class="scroll"><table><thead><tr><th>תוכנה</th><th>מקור</th><th>נעולה</th><th>בלי אינטרנט</th><th>גישה ישירה</th></tr></thead><tbody id="catalog"></tbody></table></div>
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
<section class="wide"><h2>אתרים שלא נבדקים לעומק (בנקים ואתרים רגישים)</h2>
<p class="hint">כדי לראות כתובת מלאה, Guardian פותח את התקשורת המוצפנת במחשב שלך (שום דבר לא יוצא מהמחשב). באתרים שברשימה הזו הוא לא עושה את זה, ובודק רק את שם האתר. מומלץ להוסיף כאן בנקים, קופות חולים ואתרים רשמיים. אם אתר מסוים מציג שגיאת אבטחה, הוסיפו אותו לכאן.</p>
<form class="row add" data-list="noDecrypt"><input placeholder="bankhapoalim.co.il" required style="flex:1"><button>הוספה</button></form>
<ul id="noDecrypt"></ul></section>
</div>

<div class="page" data-sub-page="netapps" hidden>
<section><h2>תוכנות בלי אינטרנט</h2>
<p class="hint">התוכנות האלה נפתחות כרגיל אבל לא יכולות להתחבר לאינטרנט. הדרך הנוחה להוסיף היא בלשונית "תוכנות". כאן אפשר גם להדביק נתיב מלא לקובץ.</p>
<form class="row add" data-list="blockedApps"><input required style="flex:1" placeholder="C:\Games\game.exe"><button>הוספה</button></form><ul id="blockedApps"></ul></section>
<section><h2>תוכנות עם גישה ישירה</h2>
<p class="hint">כש"סינון לפי כתובת מלאה" דולק, כל התקשורת עוברת דרך מסנן אחד במחשב. רוב הדפדפנים עוברים בו בלי בעיה, אבל יש תוכנות שלא יודעות לעבור דרכו ולכן מאבדות אינטרנט. תוכנות ברשימה הזו מתחברות ישירות, בלי סינון.</p>
<form class="row add" data-list="directApps"><input required style="flex:1" placeholder="C:\Program Files (x86)\Steam\steam.exe"><button>הוספה</button></form><ul id="directApps"></ul></section>
<section class="wide"><h2>תוכנות שניסו להתחבר ונחסמו</h2>
<p class="hint">במקום ניסוי וטעייה: Guardian רושם כל תוכנה שניסתה להתחבר לאינטרנט ישירות, בלי לעבור במסנן, ונחסמה. אם תוכנה שאתם צריכים מופיעה כאן ולא עובדת, לחצו "לאפשר גישה ישירה". הרשימה מתמלאת רק כש"סינון לפי כתובת מלאה" דולק.</p>
<div class="scroll"><table><thead><tr><th>תוכנה</th><th>ניסיונות</th><th>לאחרונה</th><th></th></tr></thead><tbody id="direct"></tbody></table></div>
<div id="directEmpty" class="hint" style="margin-top:8px"></div>
</section>
<section class="wide"><h2>מה בדרך כלל עובד דרך המסנן ומה לא</h2>
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
<p class="hint">לכל משתמש במחשב אפשר לקבוע שעות שבהן אסור לו להיות מחובר. כשמגיעה השעה, המשתמש מקבל הודעה כמה דקות לפני, ואז הוא מנותק והמחשב חוזר למסך הכניסה. בזמן הזה הוא גם לא יכול להיכנס.<br>
טווח שעות שעובר חצות (למשל 23:00 עד 07:00) נחשב ליום שבו הוא מתחיל. כדי לחסום יום שלם, בחרו אותה שעה בהתחלה ובסוף.</p>
<div id="allLocked" class="warn" hidden>שימו לב: לכל המשתמשים במחשב יש שעות נעילה. אם יש שעה שבה כולם נעולים, אף אחד לא יוכל להיכנס למחשב בזמן הזה, גם לא כדי לשנות הגדרות. כדאי להשאיר לפחות משתמש מנהל אחד בלי נעילה.</div>
<div id="users"></div>
</section>
</div>

<!-- ===== פעילות ===== -->
<div class="page" data-page="log" hidden>
<section class="wide"><h2>פעילות בזמן אמת</h2>
<p class="hint">כל אתר שנפתח וכל תוכנה שנחסמה מופיעים כאן. אפשר לחסום או להתיר כל שורה: רק את הכתובת המדויקת, או את כל האתר (הדומיין). לתוכנה שנחסמה אפשר להתיר רק את הקובץ, או את כל התיקייה שלה (מומלץ לתוכנות שמורכבות מכמה קבצים, כמו Discord או Zoom).</p>
<div class="row"><label><input type="checkbox" id="onlyBlocked"> רק מה שנחסם</label><input id="filter" placeholder="סינון…" style="flex:1;min-width:160px"><label><input type="checkbox" id="pause"> עצירת עדכון</label></div>
<div class="scroll" style="margin-top:8px"><table><thead><tr><th>שעה</th><th>סוג</th><th>מצב</th><th>מה</th><th>פעולות</th></tr></thead><tbody id="log"></tbody></table></div></section>
</div>

<!-- ===== הגדרות ===== -->
<div class="page" data-page="settings" hidden>
<section><h2>נעילת הממשק</h2>
<p class="hint">אם לא נוגעים בחלון הזה במשך הזמן שנבחר, הוא ננעל ושוב צריך סיסמה. כך אף אחד לא יכול לשנות הגדרות אם שכחתם את החלון פתוח.</p>
<div class="row"><label>לנעול אחרי <select id="idle"></select> דקות ללא פעילות</label></div></section>
<section><h2>מצב החיבור</h2>
<p class="hint">כאן רואים אם Guardian מצליח לברר כתובות אתרים. אם יש "מחובר, אין אינטרנט", הסתכלו כאן קודם.</p>
<div id="health" class="hint"></div><button class="ghost" id="healthBtn">בדיקה</button></section>
</div>
</main>
</div>
<div id="toast" hidden></div>

<script>
const $=id=>document.getElementById(id);
const el=(tag,props={},...kids)=>{const e=document.createElement(tag);for(const[k,v]of Object.entries(props)){if(k==='class')e.className=v;else if(k.startsWith('on'))e[k]=v;else if(k in e)e[k]=v;else e.setAttribute(k,v)}e.append(...kids.filter(x=>x!=null));return e};
const HE={'Wrong password.':'הסיסמה שגויה.','Too many attempts. Try again later.':'יותר מדי ניסיונות. נסו שוב בעוד כמה דקות.','Login required.':'צריך להיכנס שוב.','Invalid value.':'הערך לא תקין.','Not in list.':'לא נמצא ברשימה.','Invalid program.':'התוכנה לא נמצאה.','Invalid user name.':'שם המשתמש לא תקין.','Pick at least one day for each time range.':'צריך לבחור לפחות יום אחד בכל טווח שעות.','Warning must be 0-60 minutes.':'זמן ההתראה צריך להיות בין 0 ל-60 דקות.','Internal error.':'שגיאה פנימית. נסו שוב.'};
let idleMin=5,lastAct=Date.now(),lastPing=Date.now(),state=null;
function toast(msg,bad){const t=$('toast');t.textContent=msg;t.className=bad?'bad':'';t.hidden=false;clearTimeout(t._t);t._t=setTimeout(()=>t.hidden=true,4500)}
function api(p,o={},active=true){const h={'X-Guardian':'1','Content-Type':'application/json'};if(active)h['X-Active']='1';
 return fetch(p,{...o,headers:h,credentials:'same-origin'}).then(async r=>{const j=await r.json().catch(()=>({}));if(r.status===401&&p!=='/api/login'){show(false)}if(!r.ok)throw new Error(HE[j.error]||j.error||('שגיאה '+r.status));return j})}
const post=(p,b)=>api(p,{method:'POST',body:JSON.stringify(b)});
const fail=x=>toast(x.message,true);

function show(app){$('app').hidden=!app;$('login').hidden=app;if(app){lastAct=Date.now();load();tick()}else{$('pw').focus()}}
$('lf').onsubmit=e=>{e.preventDefault();post('/api/login',{password:$('pw').value}).then(()=>{$('pw').value='';$('lerr').textContent='';show(true)}).catch(x=>$('lerr').textContent=x.message)};
$('logout').onclick=()=>post('/api/logout',{}).finally(()=>show(false));

// Idle lock: the server also expires the session, this just locks the screen on time.
['mousemove','keydown','click','wheel','touchstart'].forEach(ev=>addEventListener(ev,()=>{lastAct=Date.now();if(!$('app').hidden&&Date.now()-lastPing>60000){lastPing=Date.now();api('/api/state').catch(()=>{})}},{passive:true}));
setInterval(()=>{if(!$('app').hidden&&Date.now()-lastAct>idleMin*60000){post('/api/logout',{}).finally(()=>{show(false);$('lerr').textContent='הממשק ננעל אחרי חוסר פעילות.'})}},10000);

// Tabs
function openTab(name){document.querySelectorAll('#tabs button').forEach(x=>x.classList.toggle('on',x.dataset.tab===name));document.querySelectorAll('[data-page]').forEach(m=>m.hidden=m.dataset.page!==name);
 if(name==='programs'&&!catalog)loadCatalog(false);if(name==='users')loadUsers();if(name==='net')loadDirect();if(name==='settings')loadHealth()}
document.querySelectorAll('#tabs button').forEach(b=>b.onclick=()=>openTab(b.dataset.tab));
document.querySelectorAll('#netsub button').forEach(b=>b.onclick=()=>{document.querySelectorAll('#netsub button').forEach(x=>x.classList.toggle('on',x===b));document.querySelectorAll('[data-sub-page]').forEach(m=>m.hidden=m.dataset.subPage!==b.dataset.sub);if(b.dataset.sub==='netapps')loadDirect()});

// Features
const TOGGLES=[
 ['apps.enabled','נעילת תוכנות','תוכנות שסימנתם כ"נעולות" בלשונית "תוכנות" ייסגרו מיד כשמישהו פותח אותן, ויופיע מסך חסימה. בלי היכולת הזו, הסימונים לא נאכפים.'],
 ['apps.defaultDeny','חסימת כל תוכנה לא מוכרת','כל תוכנה שלא הותקנה כרגיל תיחסם: קבצים שהורדו, תוכנות ניידות, דיסק און קי ותוכנות שמתקינות את עצמן בלי אישור מנהל. כדי לאפשר אחת מהן, התירו אותה מלשונית "פעילות" או הוסיפו אותה ב"תוכנות" > "הוספה ידנית".','לפני שמדליקים: כדאי להדליק קודם את "נעילת תוכנות", לעבוד יום רגיל, ולראות בלשונית "פעילות" מה היה נחסם.'],
 ['apps.trustProgramFiles','לסמוך על תוכנות שהותקנו כרגיל','תוכנות שהותקנו לתיקיית Program Files (התקנה רגילה שדורשת אישור מנהל) נחשבות מוכרות ולא נחסמות. מומלץ להשאיר דולק.'],
 ['network.enabled','סינון אתרים וחומת אש','חוסם אתרים שלמים לפי הרשימות בלשונית "רשת", ומונע אינטרנט מתוכנות שסימנתם "בלי אינטרנט". עובד בכל הדפדפנים ובכל התוכנות.'],
 ['network.urlFiltering','סינון לפי כתובת מלאה','מאפשר לחסום חלק מאתר (למשל רק youtube.com/shorts) ולא רק אתר שלם. כדי לעשות את זה Guardian מתקין תעודת אבטחה מקומית ובודק את התקשורת המוצפנת במחשב עצמו. יש תוכנות שלא יודעות לעבוד כך, והן יאבדו אינטרנט עד שתאפשרו להן "גישה ישירה" (רשת > תוכנות ברשת יראה לכם בדיוק אילו).','אחרי ההדלקה צריך לסגור ולפתוח מחדש את הדפדפנים. בדקו את "רשת" > "תוכנות ברשת" בימים הראשונים.'],
];
function sw(checked,onchange,label,disabled){const i=el('input',{type:'checkbox',checked,disabled:!!disabled});i.setAttribute('aria-label',label);i.onchange=()=>onchange(i);return el('label',{class:'switch',title:label},i,el('span',{class:'slider'}))}
function renderToggles(s){$('toggles').replaceChildren(...TOGGLES.map(([k,t,d,w])=>{const txt=el('div',{},el('b',{},t),el('small',{},d));if(w&&!s[k])txt.append(el('div',{class:'warn'},w));
 return el('div',{class:'toggle'},txt,sw(s[k],i=>post('/api/settings',{key:k,value:String(i.checked)}).then(st=>{render(st);toast(t+(i.checked?' – הודלק':' – כובה'))}).catch(x=>{fail(x);i.checked=!i.checked}),t))}))}

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
function render(st){state=st;idleMin=st.idleMinutes||5;renderToggles(st.settings);renderMode(st);for(const[k,v]of Object.entries(st.lists))if($(k))renderList(k,v);
 $('idle').replaceChildren(...st.idleChoices.map(m=>el('option',{value:m,selected:m===idleMin},String(m))));if(catalog)renderCatalog()}
function load(){api('/api/state').then(render).catch(fail)}
$('idle').onchange=e=>post('/api/settings/idle',{minutes:e.target.value}).then(st=>{render(st);toast('הממשק יינעל אחרי '+e.target.value+' דקות ללא פעילות')}).catch(fail);

// Programs (catalog)
let catalog=null;const SRCN={StartMenu:'תפריט התחל',InstalledApps:'אפליקציות מותקנות',Store:'Microsoft Store'};
function loadCatalog(refresh){$('catNote').textContent='טוען את רשימת התוכנות… (בפעם הראשונה זה יכול לקחת כחצי דקה)';api('/api/catalog'+(refresh?'?refresh=1':'')).then(c=>{catalog=c;renderCatalog()}).catch(x=>{$('catNote').textContent='';fail(x)})}
$('refresh').onclick=()=>loadCatalog(true);$('q').oninput=()=>renderCatalog();$('onlySet').onchange=()=>renderCatalog();
function setApp(a,kind,i){post('/api/catalog/set',{path:a.exePath,kind,value:String(i.checked)}).then(c=>{catalog=c;load();
 const msg={lock:['ננעלה','שוחררה מנעילה'],internet:['לא תוכל להתחבר לאינטרנט','יכולה שוב להתחבר לאינטרנט'],direct:['קיבלה גישה ישירה לאינטרנט','תעבור שוב דרך הסינון']}[kind];toast(a.name+' '+(i.checked?msg[0]:msg[1]))}).catch(x=>{fail(x);i.checked=!i.checked})}
function renderCatalog(){if(!catalog)return;const q=$('q').value.trim().toLowerCase(),only=$('onlySet').checked,direct=state&&state.settings['network.urlFiltering'];
 const rows=catalog.filter(a=>(!q||a.name.toLowerCase().includes(q)||a.exePath.toLowerCase().includes(q))&&(!only||a.locked||a.internetBlocked||a.directInternet));
 $('catNote').textContent=catalog.length+' תוכנות נמצאו'+(q||only?', מוצגות '+rows.length:'')+'.'+(state&&!state.settings['apps.enabled']&&catalog.some(a=>a.locked)?' שימו לב: "נעילת תוכנות" כבויה בלשונית "יכולות", ולכן הנעילות לא נאכפות.':'');
 $('catalog').replaceChildren(...rows.map(a=>el('tr',{},el('td',{},el('b',{},a.name),el('div',{class:'hint ltr',style:'margin:0;font-size:11px'},a.exePath)),el('td',{},el('span',{class:'badge'},SRCN[a.source]||a.source)),
  el('td',{},sw(a.locked,i=>setApp(a,'lock',i),'נעולה')),el('td',{},sw(a.internetBlocked,i=>setApp(a,'internet',i),'בלי אינטרנט')),el('td',{},sw(a.directInternet,i=>setApp(a,'direct',i),'גישה ישירה',!direct&&!a.directInternet)))))}

// Direct-connection attempts
function loadDirect(){api('/api/direct',{},false).then(rows=>{$('direct').replaceChildren(...rows.map(d=>el('tr',{},el('td',{class:'l'},d.appPath),el('td',{},String(d.count)),el('td',{},new Date(d.last).toLocaleString('he-IL')),
  el('td',{},d.allowed?el('span',{class:'ok'},'מאושרת'):el('button',{class:'link',onclick:()=>edit('directApps','add',d.appPath).then(()=>{toast('התוכנה קיבלה גישה ישירה. אם היא עדיין לא מתחברת, סגרו ופתחו אותה מחדש.');loadDirect()}).catch(()=>{})},'לאפשר גישה ישירה')))));
 $('directEmpty').textContent=rows.length?'':(state&&state.settings['network.urlFiltering']?'עדיין לא נחסמו תוכנות. מצוין.':'"סינון לפי כתובת מלאה" כבוי, ולכן אין מה להציג.')}).catch(()=>{})}

// Users
const DAYS=[['Sunday','א'],['Monday','ב'],['Tuesday','ג'],['Wednesday','ד'],['Thursday','ה'],['Friday','ו'],['Saturday','ש']];
function winRow(w){const d=el('span',{class:'days'},...DAYS.map(([v,l])=>el('label',{},el('input',{type:'checkbox',value:v,checked:w.days.includes(v)}),l)));
 const r=el('div',{class:'win'},d,'מ-',el('input',{type:'time',value:w.start,class:'s'}),'עד',el('input',{type:'time',value:w.end,class:'e'}),el('button',{class:'link',onclick:()=>r.remove()},'מחיקה'));return r}
function readWins(box){return[...box.querySelectorAll('.win')].map(r=>({days:[...r.querySelectorAll('.days input:checked')].map(i=>i.value),start:r.querySelector('.s').value,end:r.querySelector('.e').value}))}
function loadUsers(){api('/api/users').then(u=>{$('allLocked').hidden=!u.everyoneLocked;$('users').replaceChildren(...u.accounts.map(a=>{
 const wins=a.rule?a.rule.lockedDuring.windows:[];const box=el('div');wins.forEach(w=>box.append(winRow(w)));
 const warn=el('input',{type:'number',min:0,max:60,value:a.rule?a.rule.warningMinutes:5,style:'width:70px'});
 const status=a.unlockedUntil?el('span',{class:'badge ok'},'שוחרר זמנית עד '+new Date(a.unlockedUntil).toLocaleTimeString('he-IL',{hour:'2-digit',minute:'2-digit'})):wins.length?el('span',{class:'badge'},'יש שעות נעילה'):el('span',{class:'badge'},'ללא הגבלה');
 const save=()=>post('/api/users/set',{user:a.name,windows:JSON.stringify(readWins(box)),warning:warn.value}).then(()=>{toast(readWins(box).length?'שעות הנעילה של '+a.name+' נשמרו':'הנעילה של '+a.name+' בוטלה');load();loadUsers()}).catch(fail);
 const mins=el('select',{},...[15,30,60,120,240].map(m=>el('option',{value:m},m<60?m+' דקות':(m/60)+' שעות')));
 const unlock=el('div',{class:'row',style:'margin-top:8px'},el('span',{class:'muted'},'שחרור זמני:'),mins,
  el('button',{class:'ghost',onclick:()=>post('/api/users/unlock',{user:a.name,minutes:mins.value}).then(()=>{toast(a.name+' יכול להשתמש במחשב גם בשעות הנעילה, למשך '+mins.selectedOptions[0].textContent);loadUsers()}).catch(fail)},'לשחרר'),
  a.unlockedUntil?el('button',{class:'link',onclick:()=>post('/api/users/unlock',{user:a.name,minutes:'0'}).then(()=>{toast('השחרור הזמני בוטל');loadUsers()}).catch(fail)},'ביטול השחרור'):null);
 return el('div',{class:'user'},el('div',{class:'row'},el('b',{},a.name),status),
  el('div',{class:'hint',style:'margin:6px 0 0'},'שעות שבהן '+a.name+' לא יכול להיות מחובר:'),box,
  el('div',{class:'row',style:'margin-top:8px'},el('button',{class:'ghost',onclick:()=>box.append(winRow({days:DAYS.slice(0,5).map(d=>d[0]),start:'23:00',end:'07:00'}))},'+ טווח שעות'),
   el('label',{},'התראה ',warn,' דקות לפני הניתוק'),el('button',{onclick:save},'שמירה')),wins.length?unlock:null)}))}).catch(fail)}

// Activity
const SRC={web:'אתר',dns:'אתר',app:'תוכנה',direct:'חיבור ישיר'};
const hostOf=t=>{try{return new URL(/^[a-z]+:\/\//i.test(t)?t:'http://'+t).hostname}catch{return t}};
const isFullUrl=t=>/^[a-z]+:\/\/[^/]+\/./i.test(t)||(/^[^:/]+\/./.test(t));
function site(target,allow,whole){return post('/api/site',{target,allow:String(allow),whole:String(whole)}).then(st=>{render(st);
 toast((whole?'האתר '+hostOf(target):'הכתובת')+(allow?' הותר':' נחסם')+(whole?'':'ה')+'. השינוי ייכנס לתוקף בטעינה הבאה של הדף.')}).catch(fail)}
function appAllow(path,scope){return post('/api/apps/allow',{path,scope}).then(r=>{load();toast((scope==='folder'?'כל התוכנות בתיקייה הותרו: ':'התוכנה הותרה: ')+r.allowed+'. אפשר לפתוח אותה שוב.')}).catch(fail)}
const btn=(t,f)=>el('button',{class:'link',onclick:f},t);
function actions(r){const t=r.target;
 if(r.source==='web'||r.source==='dns'){const b=[];const h=hostOf(t);
  if(isFullUrl(t))b.push(r.allowed?btn('לחסום כתובת זו',()=>site(t,false,false)):btn('להתיר כתובת זו',()=>site(t,true,false)));
  b.push(r.allowed?btn('לחסום את כל '+h,()=>site(t,false,true)):btn('להתיר את כל '+h,()=>site(t,true,true)));return b}
 if(r.source==='app'&&!r.allowed)return[btn('להתיר תוכנה זו',()=>appAllow(t,'file')),btn('להתיר את כל התיקייה',()=>appAllow(t,'folder'))];
 if(r.source==='direct')return[btn('לאפשר גישה ישירה',()=>edit('directApps','add',t).then(()=>toast('התוכנה קיבלה גישה ישירה')).catch(()=>{}))];
 return[]}
let timer;function tick(){clearTimeout(timer);if($('app').hidden)return;if($('pause').checked||document.querySelector('[data-page=log]').hidden){timer=setTimeout(tick,3000);return}
 api('/api/log',{},false).then(rows=>{const ob=$('onlyBlocked').checked,q=$('filter').value.toLowerCase();
 $('log').replaceChildren(...rows.filter(r=>(!ob||!r.allowed)&&(!q||r.target.toLowerCase().includes(q))).slice(0,200).map(r=>el('tr',{},
  el('td',{},new Date(r.time).toLocaleTimeString('he-IL')),el('td',{},SRC[r.source]||r.source),el('td',{class:r.allowed?'ok':'bad'},r.allowed?'הותר':'נחסם'),
  el('td',{class:'l'},r.target),el('td',{},el('div',{class:'acts'},...actions(r))))))}).catch(()=>{}).finally(()=>timer=setTimeout(tick,3000))}
$('pause').onchange=()=>tick();

// Settings
function loadHealth(){api('/api/health').then(h=>{$('health').replaceChildren(
 h.dnsLastSuccess?el('div',{class:'ok'},'בירור כתובות עובד. הצלחה אחרונה: '+new Date(h.dnsLastSuccess).toLocaleTimeString('he-IL')):el('div',{class:'muted'},'עדיין לא היו בירורי כתובות (סינון האתרים כבוי או שהשירות רק התחיל).'),
 h.dnsLastError?el('div',{class:'bad'},'שגיאה אחרונה: '+h.dnsLastError):null)}).catch(fail)}
$('healthBtn').onclick=loadHealth;

api('/api/state',{},false).then(st=>{show(true);render(st)}).catch(()=>show(false));
</script>
</body></html>
""";
}
