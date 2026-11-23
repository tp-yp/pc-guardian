namespace Guardian.Service.Ui;

internal static class Page
{
    public const string Html = """
<!doctype html>
<html lang="he" dir="rtl">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Guardian – ניהול</title>
<style>
:root{--bg:#f6f7f9;--card:#fff;--text:#1d2330;--muted:#6b7280;--ok:#15803d;--bad:#b91c1c;--accent:#2563eb;--line:#e5e7eb;--warn:#92400e;--warnbg:#fffbeb}
@media (prefers-color-scheme:dark){:root{--bg:#111418;--card:#1b2027;--text:#e6e9ee;--muted:#9aa3af;--ok:#4ade80;--bad:#f87171;--accent:#60a5fa;--line:#2b313a;--warn:#fcd34d;--warnbg:#2a2210}}
*{box-sizing:border-box}body{margin:0;font-family:system-ui,"Segoe UI",Arial,sans-serif;background:var(--bg);color:var(--text)}
header{padding:14px 20px;display:flex;justify-content:space-between;align-items:center;border-bottom:1px solid var(--line);gap:12px;flex-wrap:wrap}
h1{font-size:18px;margin:0}nav{display:flex;gap:4px;flex-wrap:wrap}nav button{background:none;color:var(--text);border:1px solid transparent}nav button.on{border-color:var(--line);background:var(--card)}
main{max-width:1100px;margin:auto;padding:16px;display:grid;gap:16px;grid-template-columns:1fr 1fr}
@media (max-width:800px){main{grid-template-columns:1fr}}
[hidden]{display:none !important}
section{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px;min-width:0}
h2{font-size:15px;margin:0 0 6px}p.hint{color:var(--muted);font-size:13px;margin:0 0 10px;line-height:1.5}
input,select,button{font:inherit;padding:7px 10px;border-radius:7px;border:1px solid var(--line);background:var(--card);color:var(--text)}
button{background:var(--accent);color:#fff;border:0;cursor:pointer}button.link{background:none;color:var(--accent);padding:0 6px}
ul{list-style:none;padding:0;margin:8px 0 0;max-height:240px;overflow:auto}li{display:flex;justify-content:space-between;gap:8px;padding:5px 0;border-bottom:1px solid var(--line)}li span{direction:ltr;word-break:break-all}
table{width:100%;border-collapse:collapse;font-size:13px}td{padding:4px 6px;border-bottom:1px solid var(--line);word-break:break-all}td.l{direction:ltr;text-align:left}
.ok{color:var(--ok)}.bad{color:var(--bad)}.row{display:flex;gap:8px;flex-wrap:wrap;align-items:center}.wide{grid-column:1/-1}
.toggle{display:flex;justify-content:space-between;gap:12px;padding:10px 0;border-bottom:1px solid var(--line)}.toggle:last-child{border:0}
.toggle b{display:block}.toggle small{color:var(--muted);line-height:1.5}
.switch{position:relative;width:44px;height:24px;flex:none}.switch input{opacity:0;width:0;height:0}
.slider{position:absolute;inset:0;background:var(--line);border-radius:24px;transition:.2s;cursor:pointer}.slider:before{content:"";position:absolute;width:18px;height:18px;right:3px;top:3px;background:#fff;border-radius:50%;transition:.2s}
input:checked+.slider{background:var(--accent)}input:checked+.slider:before{transform:translateX(-20px)}
.warn{background:var(--warnbg);color:var(--warn);border-radius:8px;padding:8px 10px;font-size:13px;margin-top:6px}
#login{max-width:360px;margin:15vh auto;padding:0 16px}#err{color:var(--bad);min-height:1.2em;margin-top:6px}
</style>
</head>
<body>
<div id="login"><section><h2>כניסה לניהול</h2>
<form id="lf" class="row"><input id="pw" type="password" placeholder="סיסמת ניהול" autofocus required style="flex:1"><button>כניסה</button></form>
<div id="lerr" style="color:var(--bad);margin-top:6px"></div></section></div>

<div id="app" hidden>
<header><h1>Guardian</h1>
<nav><button data-tab="features" class="on">יכולות</button><button data-tab="apps">אפליקציות</button><button data-tab="web">אתרים</button><button data-tab="log">פעילות</button></nav>
<button class="link" id="logout">יציאה</button></header>
<div id="err" style="text-align:center"></div>

<main data-page="features">
<section class="wide"><h2>מה פעיל עכשיו</h2>
<p class="hint">מדליקים יכולת אחת, בודקים שהכול עובד, ורק אז ממשיכים לבאה. כל שינוי נכנס לתוקף תוך כמה שניות, וכיבוי מחזיר את המחשב למצב הקודם.</p>
<div id="toggles"></div></section>
</main>

<main data-page="apps" hidden>
<section><h2>אפליקציות מותרות</h2><p class="hint">תיקייה (C:\Users\…\AppData\Local\Discord\) או שם קובץ (zoom.exe). רלוונטי כשחסימת אפליקציות לא מוכרות פעילה.</p>
<form class="row add" data-list="allowApps"><input required style="flex:1" placeholder="נתיב או שם קובץ"><button>הוספה</button></form><ul id="allowApps"></ul></section>
<section><h2>אפליקציות חסומות</h2><p class="hint">נחסמות תמיד, גם אם הן מותקנות ב-Program Files.</p>
<form class="row add" data-list="blockApps"><input required style="flex:1" placeholder="steam.exe"><button>הוספה</button></form><ul id="blockApps"></ul></section>
<section><h2>בלי אינטרנט בכלל</h2><p class="hint">נתיב מלא ל-exe. חומת האש חוסמת לו כל תקשורת.</p>
<form class="row add" data-list="blockedApps"><input required style="flex:1" placeholder="C:\Games\game.exe"><button>הוספה</button></form><ul id="blockedApps"></ul></section>
<section><h2>גישה ישירה לאינטרנט</h2><p class="hint">כשסינון כתובות מלאות פעיל, רק תוכנות שמכבדות את ה-proxy יוצאות לאינטרנט. כאן מוסיפים משחקים, Steam, Spotify וכו'.</p>
<form class="row add" data-list="directApps"><input required style="flex:1" placeholder="C:\Program Files (x86)\Steam\steam.exe"><button>הוספה</button></form><ul id="directApps"></ul></section>
</main>

<main data-page="web" hidden>
<section><h2>כתובות</h2>
<div class="row"><label>מצב: <select id="mode"><option value="Blocklist">רשימה שחורה (הכול מותר חוץ מ…)</option><option value="Allowlist">רשימה לבנה (רק…)</option></select></label></div>
<p class="hint" style="margin-top:8px">דומיין (tiktok.com) או תחילת כתובת (youtube.com/shorts). כתובת עם נתיב נאכפת רק כשסינון כתובות מלאות פעיל.</p>
<form class="row add" data-list="domains"><input placeholder="example.com או youtube.com/shorts" required style="flex:1"><button>הוספה</button></form>
<ul id="domains"></ul></section>
<section><h2>בלי פענוח (בנקים / אפליקציות רגישות)</h2><p class="hint">אתרים אלה לא מפוענחים ומסוננים לפי דומיין בלבד.</p>
<form class="row add" data-list="noDecrypt"><input placeholder="bank.co.il" required style="flex:1"><button>הוספה</button></form>
<ul id="noDecrypt"></ul></section>
</main>

<main data-page="log" hidden>
<section class="wide"><h2>פעילות בזמן אמת</h2>
<div class="row"><label><input type="checkbox" id="onlyBlocked"> רק חסומים</label><input id="filter" placeholder="סינון…"></div>
<table><tbody id="log"></tbody></table></section>
</main>
</div>

<script>
const TOGGLES=[
 ['apps.enabled','חסימת אפליקציות','סוגר אפליקציות לפי הכללים בלשונית "אפליקציות".'],
 ['apps.defaultDeny','חסימת כל אפליקציה לא מוכרת','כל exe מחוץ ל-Windows ול-Program Files נחסם (קבצים ניידים, הורדות, דיסק און קי, תוכנות שמותקנות ל-AppData). לפני הדלקה – הוסיפו ל"מותרות" את מה שאתם צריכים.','מומלץ להדליק רק אחרי שבדקתם ב"פעילות" מה נחסם.'],
 ['apps.trustProgramFiles','לסמוך על Program Files','תוכנות שהותקנו כרגיל (דורש מנהל) מותרות.'],
 ['network.enabled','סינון אתרים לפי דומיין + חומת אש','DNS מקומי חוסם דומיינים מהרשימה, וחסימת אינטרנט לפי אפליקציה. מכבה DNS-over-HTTPS בדפדפנים.'],
 ['network.urlFiltering','סינון לפי כתובת מלאה (proxy)','מפענח HTTPS בעזרת תעודה מקומית ובודק כל כתובת. תוכנות שלא מכבדות proxy יצטרכו "גישה ישירה".','בהדלקה ייתכן שתוכנות מסוימות יאבדו חיבור עד שתוסיפו אותן לגישה ישירה. דפדפנים צריכים הפעלה מחדש.'],
];
const api=(p,o={})=>fetch(p,{...o,headers:{'X-Guardian':'1','Content-Type':'application/json'},credentials:'same-origin'}).then(async r=>{const j=await r.json().catch(()=>({}));if(r.status===401&&p!=='/api/login'){show(false)}if(!r.ok)throw new Error(j.error||r.status);return j});
const $=id=>document.getElementById(id);
const flash=m=>{$('err').textContent=m;setTimeout(()=>$('err').textContent='',5000)};
function show(app){$('app').hidden=!app;$('login').hidden=app;if(app){load();tick()}}
$('lf').onsubmit=e=>{e.preventDefault();api('/api/login',{method:'POST',body:JSON.stringify({password:$('pw').value})}).then(()=>{$('pw').value='';$('lerr').textContent='';show(true)}).catch(x=>$('lerr').textContent=x.message)};
$('logout').onclick=()=>api('/api/logout',{method:'POST'}).finally(()=>show(false));
document.querySelectorAll('nav button').forEach(b=>b.onclick=()=>{document.querySelectorAll('nav button').forEach(x=>x.classList.toggle('on',x===b));document.querySelectorAll('main').forEach(m=>m.hidden=m.dataset.page!==b.dataset.tab)});
function renderToggles(s){$('toggles').replaceChildren(...TOGGLES.map(([k,t,d,w])=>{const row=document.createElement('div');row.className='toggle';
const txt=document.createElement('div');const b=document.createElement('b');b.textContent=t;const sm=document.createElement('small');sm.textContent=d;txt.append(b,sm);
if(w&&!s[k]){const wd=document.createElement('div');wd.className='warn';wd.textContent=w;txt.append(wd)}
const sw=document.createElement('label');sw.className='switch';const i=document.createElement('input');i.type='checkbox';i.checked=s[k];i.setAttribute('aria-label',t);
i.onchange=()=>api('/api/settings',{method:'POST',body:JSON.stringify({key:k,value:String(i.checked)})}).then(render).catch(x=>{flash(x.message);i.checked=!i.checked});
const sl=document.createElement('span');sl.className='slider';sw.append(i,sl);row.append(txt,sw);return row}))}
const APP_LISTS=['allowApps','blockApps'];
function renderList(id,items){const ul=$(id);ul.replaceChildren(...items.map(v=>{const li=document.createElement('li');const s=document.createElement('span');s.textContent=v;const b=document.createElement('button');b.className='link';b.textContent='הסרה';b.onclick=()=>edit(id,'remove',v);li.append(s,b);return li}))}
function render(st){renderToggles(st.settings);$('mode').value=st.mode;for(const[k,v]of Object.entries(st.lists))if($(k))renderList(k,v)}
function load(){api('/api/state').then(render)}
function edit(list,action,value){const url=APP_LISTS.includes(list)?'/api/apps/list':'/api/network/list';return api(url,{method:'POST',body:JSON.stringify({list,action,value})}).then(render).catch(x=>flash(x.message))}
document.querySelectorAll('form.add').forEach(f=>f.onsubmit=e=>{e.preventDefault();const i=f.querySelector('input');edit(f.dataset.list,'add',i.value).then(()=>i.value='')});
$('mode').onchange=e=>api('/api/network/mode',{method:'POST',body:JSON.stringify({mode:e.target.value})}).then(render);
const SRC={web:'אתר',dns:'DNS',app:'אפליקציה'};
let timer;function tick(){clearTimeout(timer);if($('app').hidden)return;api('/api/log').then(rows=>{const ob=$('onlyBlocked').checked,q=$('filter').value.toLowerCase();
$('log').replaceChildren(...rows.filter(r=>(!ob||!r.allowed)&&(!q||r.target.toLowerCase().includes(q))).slice(0,200).map(r=>{const tr=document.createElement('tr');
[new Date(r.time).toLocaleTimeString('he-IL'),SRC[r.source]||r.source,r.allowed?'✓':'✗'].forEach((t,i)=>{const td=document.createElement('td');td.textContent=t;if(i===2)td.className=r.allowed?'ok':'bad';tr.append(td)});
const td=document.createElement('td');td.className='l';td.textContent=r.target;tr.append(td);
const act=document.createElement('td');if(r.source==='app'&&!r.allowed){const b=document.createElement('button');b.className='link';b.textContent='לאפשר';b.onclick=()=>edit('allowApps','add',r.target);act.append(b)}tr.append(act);return tr}))}).catch(()=>{}).finally(()=>timer=setTimeout(tick,3000))}
api('/api/state').then(st=>{show(true);render(st)}).catch(()=>show(false));
</script>
</body></html>
""";
}
