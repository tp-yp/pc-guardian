namespace Guardian.Service.Ui;

internal static class SetupPages
{
    private const string Head = """
<!doctype html><html lang="he" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Guardian</title><style>
:root{--bg:#f6f7f9;--card:#fff;--text:#1d2330;--muted:#6b7280;--bad:#b91c1c;--accent:#2563eb;--line:#e5e7eb;--okbg:#ecfdf5}
@media (prefers-color-scheme:dark){:root{--bg:#111418;--card:#1b2027;--text:#e6e9ee;--muted:#9aa3af;--bad:#f87171;--accent:#60a5fa;--line:#2b313a;--okbg:#12301f}}
*{box-sizing:border-box}body{margin:0;font-family:system-ui,"Segoe UI",Arial,sans-serif;background:var(--bg);color:var(--text);display:grid;place-items:center;min-height:100vh;padding:16px}
.card{background:var(--card);border:1px solid var(--line);border-radius:12px;padding:24px;width:100%;max-width:440px}
h1{font-size:20px;margin:0 0 6px}p{color:var(--muted);margin:0 0 16px;line-height:1.5}label{display:block;margin:12px 0 4px;font-size:14px}
input[type=password]{width:100%;font:inherit;padding:9px 11px;border-radius:8px;border:1px solid var(--line);background:var(--card);color:var(--text);direction:ltr}
.check{display:flex;gap:8px;align-items:flex-start;margin-top:14px;font-size:14px}
button{margin-top:18px;width:100%;font:inherit;padding:10px;border:0;border-radius:8px;background:var(--accent);color:#fff;cursor:pointer}button:disabled{opacity:.6}
#msg{min-height:1.3em;margin-top:12px;color:var(--bad);font-size:14px}.done{background:var(--okbg);border-radius:8px;padding:14px;line-height:1.6}
</style></head><body><div class="card">
""";

    private const string Script = """
<script>
const $=id=>document.getElementById(id);
async function post(data){const r=await fetch(location.pathname,{method:'POST',headers:{'X-Guardian':'1','Content-Type':'application/json'},body:JSON.stringify(data)});return r.json().catch(()=>({ok:false,message:'שגיאה'}))}
</script>
""";

    public const string Install = Head + """
<h1>התקנת Guardian</h1>
<p>שלושה דברים קצרים וזהו. אחרי ההתקנה <b>שום דבר עוד לא נחסם</b> – בוחרים מה לחסום במסך הניהול, צעד אחרי צעד.</p>
<form id="f">
<label for="a1">סיסמת ניהול – בה נכנסים למסך הניהול (8 תווים לפחות)</label><input id="a1" type="password" autocomplete="new-password" required minlength="8">
<label for="a2">סיסמת ניהול – שוב</label><input id="a2" type="password" autocomplete="new-password" required>
<label for="r1">סיסמה מצילה – "מפתח רזרבי" שפותח הכול, גם אם שוכחים את סיסמת הניהול</label><input id="r1" type="password" autocomplete="new-password" required minlength="8">
<label for="r2">סיסמה מצילה – שוב</label><input id="r2" type="password" autocomplete="new-password" required>
<label for="idle">מסך הניהול יינעל לבד אחרי כמה זמן בלי שימוש?</label>
<select id="idle" required style="width:100%;font:inherit;padding:9px 11px;border-radius:8px;border:1px solid var(--line);background:var(--card);color:var(--text)">
<option value="" selected disabled>בחר…</option><option value="1">דקה</option><option value="3">3 דקות</option><option value="5">5 דקות</option><option value="10">10 דקות</option><option value="15">15 דקות</option><option value="30">30 דקות</option></select>
<div class="check"><input id="saved" type="checkbox"><label for="saved" style="margin:0">רשמתי את הסיסמה המצילה במקום בטוח (נייר, כספת, או אצל אדם אחר)</label></div>
<button id="go">התקנה</button><div id="msg"></div></form>
<div id="ok" class="done" hidden>✓ Guardian הותקן. עוד רגע נפתח מסך הניהול…<br>אפשר לפתוח אותו תמיד מתפריט התחל: <b>Guardian</b>.</div>
""" + Script + """
<script>
$('f').onsubmit=async e=>{e.preventDefault();const a=$('a1').value,r=$('r1').value;let m='';
if(a.length<8||r.length<8)m='כל סיסמה צריכה להיות באורך 8 תווים לפחות.';else if(a!==$('a2').value)m='שתי סיסמאות הניהול לא זהות.';
else if(r!==$('r2').value)m='שתי הסיסמאות המצילות לא זהות.';else if(a===r)m='הסיסמה המצילה חייבת להיות שונה מסיסמת הניהול.';
else if(!$('idle').value)m='בחר אחרי כמה זמן מסך הניהול יינעל.';
else if(!$('saved').checked)m='סמן שרשמת את הסיסמה המצילה – בלעדיה אי אפשר להסיר את התוכנה אם שוכחים את סיסמת הניהול.';
$('msg').textContent=m;if(m)return;$('go').disabled=true;$('go').textContent='מתקין… (עד חצי דקה)';
const res=await post({admin:a,recovery:r,idle:$('idle').value});
if(res.ok){$('f').hidden=true;$('ok').hidden=false;setTimeout(()=>location.href=res.ui,6000)}else{$('msg').textContent=res.message||'ההתקנה נכשלה';$('go').disabled=false;$('go').textContent='התקנה'}};
</script></div></body></html>
""";

    public const string Update = Head + """
<h1>עדכון Guardian</h1>
<p>נמצאה גרסה מותקנת. העדכון שומר את כל ההגדרות ואת הסיסמאות. במשך כמה שניות החסימות לא יפעלו.</p>
<form id="f"><label for="p">סיסמת ניהול או סיסמה מצילה</label><input id="p" type="password" required autofocus>
<button id="go">עדכון</button><div id="msg"></div></form>
<div id="ok" class="done" hidden>✓ עודכן. עוד רגע נפתח מסך הניהול…</div>
""" + Script + """
<script>
$('f').onsubmit=async e=>{e.preventDefault();$('go').disabled=true;$('go').textContent='מעדכן… (עד דקה)';
const res=await post({password:$('p').value});
if(res.ok){$('f').hidden=true;$('ok').hidden=false;setTimeout(()=>location.href=res.ui,8000)}else{$('msg').textContent=res.message||'העדכון נכשל';$('go').disabled=false;$('go').textContent='עדכון'}};
</script></div></body></html>
""";

    public const string Uninstall = Head + """
<h1>הסרת Guardian</h1>
<p>ההסרה מחזירה את המחשב בדיוק למצב שלפני ההתקנה: אינטרנט, חשבונות, תוכנות והגדרות.</p>
<form id="f"><label for="p">סיסמת ניהול או סיסמה מצילה</label><input id="p" type="password" required autofocus>
<button id="go">הסרה</button><div id="msg"></div></form>
<div id="ok" class="done" hidden>✓ Guardian הוסר. אפשר לסגור את החלון.<br><button onclick="chrome.webview&&chrome.webview.postMessage('close')" style="margin-top:10px">סגירה</button></div>
""" + Script + """
<script>
$('f').onsubmit=async e=>{e.preventDefault();$('go').disabled=true;$('go').textContent='מסיר… (עד דקה)';
const res=await post({password:$('p').value});
if(res.ok){$('f').hidden=true;$('ok').hidden=false}else{$('msg').textContent=res.message||'ההסרה נכשלה';$('go').disabled=false;$('go').textContent='הסרה'}};
</script></div></body></html>
""";
}
