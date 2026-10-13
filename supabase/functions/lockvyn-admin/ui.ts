// Same-origin, dependency-free interface. Access tokens are kept in memory only.
export function html(n: string, key: string, recovering: boolean): string {
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Lockvyn · Account administration</title>
<style nonce="${n}">
:root{color-scheme:light;font-family:Inter,system-ui,-apple-system,"Segoe UI",sans-serif;background:#f7f7fb;color:#211b38}
*{box-sizing:border-box}body{margin:0;min-height:100vh}main{max-width:1140px;margin:auto;padding:32px 22px 80px}
header{display:flex;gap:16px;align-items:center;justify-content:space-between;padding:0 0 28px}
.brand{display:flex;align-items:center;gap:12px;font-weight:750;font-size:21px}.logo{width:38px;height:38px;border-radius:12px;background:#6a5bd6;color:white;display:grid;place-items:center;font-size:22px}
section,.panel{background:white;border:1px solid #e5e3ef;border-radius:17px;box-shadow:0 4px 20px #1910330c;padding:24px;margin-bottom:20px}
h1{margin:0 0 8px;font-size:25px}h2{margin:0 0 6px;font-size:18px}p,.note{color:#716b84;line-height:1.6}
form{display:grid;gap:13px;max-width:460px}label{font-size:13px;font-weight:650;display:grid;gap:6px}
input{font:inherit;border:1px solid #d9d6e4;border-radius:10px;padding:11px 12px;width:100%;min-height:44px}
input:focus{outline:2px solid #9c91f7;outline-offset:0}
button{font:inherit;font-weight:650;cursor:pointer;border:1px solid #d8d4ed;background:white;color:#4b3fac;padding:10px 14px;border-radius:10px;min-height:42px}
button.primary{background:#6a5bd6;color:white;border-color:#6a5bd6}button:disabled{opacity:.5;cursor:not-allowed}
button:focus-visible{outline:3px solid #c2b9ff;outline-offset:2px}
.toolbar{display:flex;gap:12px;align-items:center;justify-content:space-between;flex-wrap:wrap}.toolbar input{max-width:340px}
table{border-collapse:collapse;width:100%;min-width:690px;text-align:left}
th,td{border-bottom:1px solid #efecf5;padding:13px 10px;font-size:13px}
th{font-size:12px;color:#777086;text-transform:uppercase;letter-spacing:.035em}
td small{color:#777086;display:block;margin-top:3px}
td:last-child,th:last-child{text-align:right}
.scroll{overflow-x:auto}.muted{color:#756d86}.ok{color:#176e4b}.warn{color:#b05617}
.stat{display:flex;gap:18px;flex-wrap:wrap}.stat strong{font-size:23px}
#alert{min-height:24px;font-weight:600}#alert.error{color:#ae3044}#alert.success{color:#126848}
footer{font-size:12px;color:#787284;margin-top:20px}
[hidden]{display:none!important}@media(max-width:640px){main{padding:22px 12px}section{padding:16px}h1{font-size:22px}}
</style>
</head>
<body>
<main>
<header><div class="brand"><span class="logo">◇</span>Lockvyn <span class="muted">/ Admin</span></div>
<button id="logout" hidden>Sign out</button></header>
<div id="alert" role="status" aria-live="polite"></div>
<section id="login" ${recovering?"hidden":""}>
<h1>Account administration</h1>
<p>Sign in with your approved administrator account. A verified authenticator code is required.</p>
<form id="login-form"><label>Email<input id="email" type="email" autocomplete="username" required></label>
<label>Cloud account password<input id="password" type="password" autocomplete="current-password" minlength="16" required></label>
<button class="primary" type="submit">Continue</button></form>
</section>
<section id="mfa" hidden>
<h2>Verify your authenticator</h2><p>Enter the current six-digit code from your registered TOTP app.</p>
<form id="mfa-form"><label>Authenticator code<input id="code" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength="6" required></label>
<button class="primary" type="submit">Verify and open dashboard</button></form>
</section>
<div id="dashboard" hidden>
<section><div class="toolbar"><div><h1>Users</h1><p class="note">Cloud account metadata only. Passwords, vault contents and recovery keys are never available here.</p></div>
<div class="stat"><div><strong id="total">—</strong><div class="muted">accounts</div></div></div></div></section>
<section>
<div class="toolbar"><h2>Registered accounts</h2><label>Search this page<input id="search" type="search" placeholder="Email address"></label></div>
<div class="scroll"><table><thead><tr><th>Email</th><th>Registered</th><th>Last sign-in</th><th>Email</th><th>2FA</th><th>Support</th></tr></thead>
<tbody id="users"></tbody></table></div>
<div class="toolbar" style="margin-top:20px"><button id="prev">Previous</button>
<span class="muted" id="page-label">Page 1</span><button id="next">Next</button></div>
</section>
<section><h2>What can I reset?</h2><p>Send a password recovery email for the user's <strong>cloud login account</strong>.
The user chooses the new password through their own verified email.
<strong>Master Passphrases cannot be reset by the administrator</strong>: doing so would compromise Lockvyn's end-to-end encryption model.</p></section>
</div>
<section id="recovery" ${recovering?"":"hidden"}>
<h1>Reset cloud account password</h1><p>This only changes your Lockvyn cloud sign-in password. It does not change the passphrase that encrypts your vault.</p>
<form id="recovery-form"><label>New cloud account password<input id="new-password" type="password" minlength="16" autocomplete="new-password" required></label>
<label>Confirm new password<input id="confirm-password" type="password" minlength="16" autocomplete="new-password" required></label>
<button class="primary" type="submit">Change cloud account password</button></form>
</section>
<footer>Support dashboard · No access to private vault entries · Account actions are logged</footer>
</main>
<script nonce="${n}">
"use strict";
const PUBLIC_KEY=${JSON.stringify(key)}, RECOVERY=${recovering};
const base="https://ghmbcfqiomggoxgwghjs.supabase.co";
let accessToken="", pendingFactor=null, challengeId="", page=1, accounts=[];
const get=id=>document.getElementById(id);
function alertText(message,bad=false){const el=get("alert");el.textContent=message;el.className=bad?"error":"success";}
function visible(id,yes){get(id).hidden=!yes;}
function date(s){return s?new Date(s).toLocaleDateString():"—";}
async function authFetch(path,body,verb="POST",jwt=""){
  const res=await fetch(base+"/auth/v1/"+path,{
    method:verb,cache:"no-store",
    headers:{"apikey":PUBLIC_KEY,"Content-Type":"application/json",...(jwt?{"Authorization":"Bearer "+jwt}:{})},
    body:body===null?undefined:JSON.stringify(body),
  });
  const data=await res.json().catch(()=>({}));
  if(!res.ok)throw Error(data.msg||data.error_description||data.message||"Authentication failed");
  return data;
}
async function api(action,params){
  const res=await fetch(base+"/functions/v1/lockvyn-admin",{method:"POST",cache:"no-store",
    headers:{"Authorization":"Bearer "+accessToken,"Content-Type":"application/json"},
    body:JSON.stringify({action,...params})});
  const data=await res.json();
  if(!res.ok)throw Error(data.error||"Request failed");
  return data;
}
function jwtAal(jwt){try{const bits=jwt.split(".")[1].replace(/-/g,"+").replace(/_/g,"/");return JSON.parse(atob(bits)).aal||"";}catch{return "";}}
async function openDashboard(){
  visible("login",false);visible("mfa",false);visible("dashboard",true);visible("logout",true);
  await loadPage(1);
}
async function setupMfa(){
  const user=await authFetch("user",null,"GET",accessToken);
  const factors=Array.isArray(user.factors)?user.factors:[];
  pendingFactor=factors.find(f=>f.factor_type==="totp" && f.status==="verified");
  if(!pendingFactor)throw Error("Enable 2FA on this account in Lockvyn before opening the admin dashboard.");
  const response=await authFetch("factors/"+encodeURIComponent(pendingFactor.id)+"/challenge",{}, "POST",accessToken);
  challengeId=response.id;
  visible("login",false);visible("mfa",true);
}
get("login-form").addEventListener("submit",async e=>{
  e.preventDefault();alertText("");
  try{
    const out=await authFetch("token?grant_type=password",{
      email:get("email").value.trim(),password:get("password").value,
    });
    get("password").value="";
    accessToken=out.access_token;
    if(jwtAal(accessToken)==="aal2")await openDashboard();else await setupMfa();
  }catch(error){alertText(error.message,true);}
});
get("mfa-form").addEventListener("submit",async e=>{
  e.preventDefault();alertText("");
  try{
    const out=await authFetch("factors/"+encodeURIComponent(pendingFactor.id)+"/verify",
      {challenge_id:challengeId,code:get("code").value.trim()},"POST",accessToken);
    accessToken=out.access_token;
    get("code").value="";
    if(jwtAal(accessToken)!=="aal2")throw Error("Verification did not establish AAL2.");
    await openDashboard();
  }catch(error){alertText(error.message,true);}
});
function td(row,value){const cell=document.createElement("td");cell.textContent=value;row.append(cell);return cell;}
function display(){
  const body=get("users");body.replaceChildren();
  const search=get("search").value.toLowerCase().trim();
  for(const user of accounts.filter(u=>u.email.toLowerCase().includes(search))){
    const row=document.createElement("tr");
    td(row,user.email);td(row,date(user.createdAt));td(row,date(user.lastSignInAt));
    td(row,user.confirmed?"Verified":"Pending");td(row,user.mfa===true?"Enabled":user.mfa===false?"Off":"Unknown");
    const action=document.createElement("td"),btn=document.createElement("button");
    btn.textContent="Send recovery email";btn.disabled=!user.confirmed;
    btn.addEventListener("click",()=>sendRecovery(user));action.append(btn);row.append(action);body.append(row);
  }
}
async function loadPage(n){
  try{
    const data=await api("list",{page:n});page=n;accounts=data.users;
    get("page-label").textContent="Page "+page;
    get("total").textContent=data.total??"—";
    get("prev").disabled=page===1;get("next").disabled=accounts.length<30;
    display();alertText("");
  }catch(error){alertText(error.message,true);}
}
async function sendRecovery(user){
  if(!confirm("Send a cloud password recovery email to "+user.email+"? This will not change their master passphrase."))return;
  try{await api("recover",{id:user.id});alertText("Recovery email requested for "+user.email);}
  catch(error){alertText(error.message,true);}
}
get("search").addEventListener("input",display);
get("prev").addEventListener("click",()=>{if(page>1)loadPage(page-1);});
get("next").addEventListener("click",()=>loadPage(page+1));
get("logout").addEventListener("click",()=>{accessToken="";accounts=[];visible("dashboard",false);visible("login",true);visible("logout",false);alertText("Signed out.");});
if(RECOVERY){
  const params=new URLSearchParams(location.hash.slice(1));
  const token=params.get("access_token"),type=params.get("type");
  history.replaceState(null,"",location.pathname);
  if(type!=="recovery"||!token){alertText("Invalid or expired recovery link.",true);get("recovery-form").hidden=true;}
  else{
    get("recovery-form").addEventListener("submit",async e=>{
      e.preventDefault();alertText("");
      const first=get("new-password").value,second=get("confirm-password").value;
      if(first!==second||first.length<16){alertText("Use matching passwords with at least 16 characters.",true);return;}
      try{await authFetch("user",{password:first},"PUT",token);
        get("new-password").value="";get("confirm-password").value="";
        alertText("Cloud account password updated. Sign in to Lockvyn with your new cloud password.");
      }catch(error){alertText(error.message,true);}
    });
  }
}
</script>
</body></html>`;
}
