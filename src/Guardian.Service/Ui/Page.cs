namespace Guardian.Service.Ui;

internal static class Page
{
    public const string Html = """
<!doctype html>
<html lang="he" dir="rtl">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Guardian – ניהול רשת</title>
<style>
:root{--bg:#f6f7f9;--card:#fff;--text:#1d2330;--muted:#6b7280;--ok:#15803d;--bad:#b91c1c;--accent:#2563eb;--line:#e5e7eb}
@media (prefers-color-scheme:dark){:root{--bg:#111418;--card:#1b2027;--text:#e6e9ee;--muted:#9aa3af;--ok:#4ade80;--bad:#f87171;--accent:#60a5fa;--line:#2b313a}}
*{box-sizing:border-box}body{margin:0;font-family:system-ui,"Segoe UI",Arial,sans-serif;background:var(--bg);color:var(--text)}
header{padding:16px 20px;display:flex;justify-content:space-between;align-items:center;border-bottom:1px solid var(--line)}
h1{font-size:18px;margin:0}main{max-width:1100px;margin:auto;padding:16px;display:grid;gap:16px;grid-template-columns:1fr 1fr}
@media (max-width:800px){main{grid-template-columns:1fr}}
section{background:var(--card);border:1px solid var(--line);border-radius:10px;padding:14px}
h2{font-size:15px;margin:0 0 10px}input,select,button{font:inherit;padding:7px 10px;border-radius:7px;border:1px solid var(--line);background:var(--card);color:var(--text)}
button{background:var(--accent);color:#fff;border:0;cursor:pointer}button.link{background:none;color:var(--accent);padding:0 6px}
ul{list-style:none;padding:0;margin:8px 0 0;max-height:260px;overflow:auto}li{display:flex;justify-content:space-between;padding:5px 0;border-bottom:1px solid var(--line);direction:ltr}
table{width:100%;border-collapse:collapse;font-size:13px}td{padding:4px 6px;border-bottom:1px solid var(--line);direction:ltr;text-align:left;word-break:break-all}
.ok{color:var(--ok)}.bad{color:var(--bad)}.muted{color:var(--muted)}.row{display:flex;gap:8px;flex-wrap:wrap}.wide{grid-column:1/-1}
#login{max-width:360px;margin:15vh auto}#err{color:var(--bad);min-height:1.2em}
</style>
</head>
<body>
<div id="login"><section><h2>כניסה לניהול</h2>
<form id="lf" class="row"><input id="pw" type="password" placeholder="סיסמת ניהול" autofocus required style="flex:1"><button>כניסה</button></form>
<div id="err"></div></section></div>

<div id="app" hidden>
<header><h1>Guardian – ניהול רשת</h1><button class="link" id="logout">יציאה</button></header>
<main>
<section><h2>כתובות</h2>
<div class="row"><label>מצב: <select id="mode"><option value="Blocklist">רשימה שחורה (הכול מותר חוץ מ…)</option><option value="Allowlist">רשימה לבנה (רק…)</option></select></label></div>
<form class="row add" data-list="domains" style="margin-top:8px"><input placeholder="example.com או youtube.com/shorts" required style="flex:1"><button>הוספה</button></form>
<ul id="domains"></ul></section>
<section><h2>בלי פענוח (בנקים / אפליקציות רגישות)</h2>
<form class="row add" data-list="noDecrypt"><input placeholder="bank.co.il" required style="flex:1"><button>הוספה</button></form>
<ul id="noDecrypt"></ul></section>
<section class="wide"><h2>גישה לרשת – בזמן אמת</h2>
<div class="row"><label><input type="checkbox" id="onlyBlocked"> רק חסומים</label><input id="filter" placeholder="סינון…"></div>
<table><tbody id="log"></tbody></table></section>
</main></div>

<script>
const api=(p,o={})=>fetch(p,{...o,headers:{'X-Guardian':'1','Content-Type':'application/json'},credentials:'same-origin'}).then(async r=>{const j=await r.json().catch(()=>({}));if(r.status===401&&p!=='/api/login'){show(false)}if(!r.ok)throw new Error(j.error||r.status);return j});
const $=id=>document.getElementById(id);
function show(app){$('app').hidden=!app;$('login').hidden=app;if(app){loadNet();tick()}}
$('lf').onsubmit=e=>{e.preventDefault();api('/api/login',{method:'POST',body:JSON.stringify({password:$('pw').value})}).then(()=>{$('pw').value='';$('err').textContent='';show(true)}).catch(x=>$('err').textContent=x.message)};
$('logout').onclick=()=>api('/api/logout',{method:'POST'}).finally(()=>show(false));
function renderList(id,items){const ul=$(id);ul.replaceChildren(...items.map(v=>{const li=document.createElement('li');const s=document.createElement('span');s.textContent=v;const b=document.createElement('button');b.className='link';b.textContent='הסרה';b.onclick=()=>edit(id,'remove',v);li.append(s,b);return li}))}
function renderNet(n){$('mode').value=n.mode;renderList('domains',n.domains);renderList('noDecrypt',n.noDecryptDomains)}
function loadNet(){api('/api/network').then(renderNet)}
function edit(list,action,value){return api('/api/network/list',{method:'POST',body:JSON.stringify({list,action,value})}).then(renderNet).catch(x=>alertBox(x.message))}
function alertBox(m){$('err').textContent=m;console.warn(m)}
document.querySelectorAll('form.add').forEach(f=>f.onsubmit=e=>{e.preventDefault();const i=f.querySelector('input');edit(f.dataset.list,'add',i.value).then(()=>i.value='')});
$('mode').onchange=e=>api('/api/network/mode',{method:'POST',body:JSON.stringify({mode:e.target.value})}).then(renderNet);
let timer;function tick(){clearTimeout(timer);if($('app').hidden)return;api('/api/log').then(rows=>{const ob=$('onlyBlocked').checked,q=$('filter').value.toLowerCase();
$('log').replaceChildren(...rows.filter(r=>(!ob||!r.allowed)&&(!q||r.target.toLowerCase().includes(q))).slice(0,200).map(r=>{const tr=document.createElement('tr');
[new Date(r.time).toLocaleTimeString('he-IL'),r.source,r.allowed?'✓':'✗',r.target].forEach((t,i)=>{const td=document.createElement('td');td.textContent=t;if(i===2)td.className=r.allowed?'ok':'bad';tr.append(td)});return tr}))}).catch(()=>{}).finally(()=>timer=setTimeout(tick,3000))}
api('/api/network').then(n=>{show(true);renderNet(n)}).catch(()=>show(false));
</script>
</body></html>
""";
}
