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

/* Phase 01: responsive account overview and explicit global-vs-filtered totals */
.overview{background:linear-gradient(135deg,#fff 0%,#f1efff 100%);border-color:#e3dafa}
.eyebrow{font-size:11px;letter-spacing:.085em;text-transform:uppercase;color:#7467aa;font-weight:750}
.cards{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:12px;margin:18px 0 24px}
.metric{border:1px solid #e4e0f1;background:white;padding:16px;border-radius:14px}
.metric strong{font-size:28px;line-height:1.4;display:block;font-variant-numeric:tabular-nums}
.metric small{color:#78718a;line-height:1.5}
.filters{display:flex;gap:10px;align-items:end;flex-wrap:wrap;margin:18px 0 12px}
.filters label{flex:1 1 170px;max-width:370px}.filters select{min-height:44px;padding:10px 12px;border:1px solid #d9d6e4;border-radius:10px;background:white;color:#211b38;font:inherit;width:100%}
.filters .search-label{flex:2 1 230px;max-width:none}
.table-head{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap}
#directory-state{font-size:13px;margin:12px 0;min-height:20px;color:#716b84}
#empty{padding:32px 12px;text-align:center;color:#716b84}
#directory-table[aria-busy="true"]{opacity:.6}
.status{font-weight:650;font-size:12px;padding:5px 9px;border-radius:7px;display:inline-block;background:#f0eef8}
.status.yes{background:#e4f6eb;color:#13613d}
.status.no{background:#fff0ea;color:#94482c}
.status.unknown{background:#f0eef5;color:#615c70}
@media(max-width:800px){.cards{grid-template-columns:repeat(2,minmax(0,1fr))}.metric{padding:12px}}
@media(max-width:520px){.filters label{max-width:none;flex:1 1 100%}.cards{gap:8px}.metric strong{font-size:23px}main{padding:18px 12px 70px}.table-head{align-items:start}}
.skip-link{position:absolute;left:12px;top:-70px;background:#fff;border:2px solid #6a5bd6;padding:12px;z-index:20;border-radius:8px}
.skip-link:focus{top:8px}
#directory-table:focus-visible{outline:3px solid #6a5bd6;outline-offset:3px}
@media(prefers-reduced-motion:reduce){*{scroll-behavior:auto!important;transition:none!important}}
/* Phase 04: concise metadata-only security signals. */
.security-alerts{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;margin:16px 0}
.security-alert{border:1px solid #e4e0f1;border-radius:12px;padding:13px;background:#fbfaff}
.security-alert strong{display:block;font-size:22px;font-variant-numeric:tabular-nums}
.security-alert small{color:#716b84;line-height:1.5}
.security-alert.warning{border-color:#e6b08a;background:#fff8f2}
.security-events{list-style:none;padding:0;display:grid;gap:9px}
.security-events li{border:1px solid #eeeaf7;border-radius:10px;padding:12px}
.security-events li strong{display:block}
.security-events li p{margin:5px 0 0;font-size:12px;overflow-wrap:anywhere}
@media(max-width:700px){.security-alerts{grid-template-columns:repeat(2,minmax(0,1fr))}}
@media(max-width:420px){.security-alerts{grid-template-columns:1fr}}
/* Phase 02: explicit, accessible, cloud-only support workflow. */
.support-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px}
.support-grid label{display:grid;gap:7px;font-size:13px;font-weight:650}
.support-grid select{width:100%;padding:10px;border:1px solid #d9d6e4;border-radius:10px;min-height:44px;background:white;color:#211b38}
.support-actions{display:flex;gap:12px;align-items:center;flex-wrap:wrap;margin-top:16px}
.support-note{font-size:13px;line-height:1.6;padding:12px;border:1px solid #e4e0f1;border-radius:9px}
.case-history{list-style:none;padding:0;display:grid;gap:8px}
.case-history li{padding:12px;border:1px solid #eeeaf7;border-radius:9px;font-size:13px;overflow-wrap:anywhere}
.device-inventory{border-top:1px solid #eeeaf7;margin-top:22px;padding-top:20px}
.device-list{list-style:none;padding:0;display:grid;gap:10px}
.device-list li{padding:13px;border:1px solid #eeeaf7;border-radius:10px}
.device-list h4{margin:0 0 5px;font-size:14px}
.device-list p{margin:4px 0;font-size:13px}
.device-list button{margin-top:8px}

@media(max-width:620px){.support-grid{grid-template-columns:1fr}}
</style>
</head>
<body>
<a class="skip-link" href="#directory-section">Skip to account directory</a>
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
<section class="overview">
  <div class="eyebrow">Overview · Account administration</div>
  <div class="toolbar"><div><h1>Users & access</h1>
    <p class="note">Live cloud account metadata. No passwords, vault records or recovery keys are available to administrators.</p></div>
  </div>
  <div class="cards" aria-label="All account statistics">
    <article class="metric"><small>Total registered</small><strong id="total">—</strong><small>All cloud accounts</small></article>
    <article class="metric"><small>Verified emails</small><strong id="verified-stat">—</strong><small>All cloud accounts</small></article>
    <article class="metric"><small>2FA enabled</small><strong id="mfa-stat">—</strong><small>Verified MFA enrollment</small></article>
    <article class="metric"><small>Active last 30 days</small><strong id="active-stat">—</strong><small>By last sign-in timestamp</small></article>
  </div>
</section>
<section id="directory-section" aria-labelledby="directory-heading">
  <div class="table-head"><div><h2 id="directory-heading" tabindex="-1">Account directory</h2>
    <p class="note">Search and filters cover all registered accounts, not only the current page.</p></div>
    <div class="muted" id="filtered-count" aria-live="polite">Loading account count…</div>
  </div>
  <div class="filters" role="group" aria-label="Account directory filters">
    <label class="search-label">Search email across all users
      <input id="search" type="search" placeholder="Search by email" autocomplete="off" maxlength="120"></label>
    <label>Email status
      <select id="email-filter"><option value="all">All email statuses</option>
      <option value="verified">Verified</option><option value="pending">Pending</option></select></label>
    <label>2FA status
      <select id="mfa-filter"><option value="all">All 2FA statuses</option>
      <option value="enabled">Enabled</option><option value="disabled">Off</option></select></label>
    <button id="clear-filters" type="button">Clear filters</button>
  </div>
  <div id="directory-state" role="status" aria-live="polite"></div>
  <div class="scroll" id="directory-table" role="region" aria-label="Accounts table (scroll horizontally on narrow screens)" tabindex="0" aria-busy="false">
    <table><thead><tr><th scope="col">Email</th><th scope="col">Registered</th>
    <th scope="col">Last sign-in</th><th scope="col">Email status</th>
    <th scope="col">2FA</th><th scope="col">Support</th></tr></thead>
    <tbody id="users"></tbody></table>
  </div>
  <div id="empty" hidden role="status">No accounts match these filters. Try clearing the search.</div>
  <button id="retry-directory" type="button" hidden>Retry loading accounts</button>
  <div class="toolbar" style="margin-top:20px">
    <button id="prev" type="button">Previous</button>
    <span class="muted" id="page-label">Page 1</span>
    <button id="next" type="button">Next</button>
  </div>
</section>
<section id="account-support" hidden aria-labelledby="support-heading">
  <h2 id="support-heading" tabindex="-1">Cloud account support</h2>
  <p id="support-target" class="note"></p>
  <p id="support-cloud-state" class="support-note" role="status" aria-live="polite">Cloud login status has not been checked.</p>
  <button id="refresh-support-status" type="button">Refresh cloud login status</button>
  <p class="support-note">All support actions affect the cloud account only, never the encrypted vault on an offline device.
    A suspended user's existing access token may remain valid until its expiry.
    2FA recovery and account deletion create review cases only; they never silently change the account. A user can <a href="https://lockvyn-admin-ui.r3dzf-studio.workers.dev/security">revoke their own cloud sessions</a> with their credentials; administrators cannot impersonate them.</p>
  <section class="device-inventory" aria-labelledby="cloud-device-heading">
    <div class="toolbar"><div><h3 id="cloud-device-heading" tabindex="-1">Registered cloud sessions</h3>
      <p class="note">Only sessions the user explicitly opted in appear here. Names and software versions are self-reported; they do not prove a physical device's identity.</p></div>
      <button id="refresh-cloud-devices" type="button">Refresh sessions</button></div>
    <p class="support-note">Revoking blocks this signed-in app session from cloud snapshots and pairing. It does not erase offline vault data or invalidate the Auth refresh token. A new sign-in creates a different session.</p>
    <section class="sync-overview" aria-labelledby="cloud-sync-heading">
      <h4 id="cloud-sync-heading">Encrypted cloud sync</h4>
      <p id="cloud-sync-usage" role="status" aria-live="polite">Sync usage has not been loaded.</p>
      <p id="cloud-sync-state" class="support-note" role="status" aria-live="polite">Cloud sync state has not been loaded.</p>
      <div class="filters" role="group" aria-label="Cloud sync activity filters">
        <label>Platform
          <select id="cloud-device-platform"><option value="">All platforms</option>
            <option value="windows">Windows</option><option value="android">Android</option>
            <option value="macos">macOS</option><option value="ios">iOS</option>
            <option value="linux">Linux</option><option value="other">Other</option></select></label>
        <label>Last result
          <select id="cloud-device-outcome"><option value="all">All results</option>
            <option value="success">Success</option><option value="failure">Failure</option>
            <option value="unknown">Not reported</option></select></label>
        <label>Activity window
          <select id="cloud-device-since"><option value="7">7 days</option><option value="30" selected>30 days</option>
            <option value="90">90 days</option><option value="365">365 days</option>
            <option value="all">Any time</option></select></label>
        <label>Device or app
          <input id="cloud-device-query" type="search" maxlength="80" autocomplete="off"
            placeholder="Filter session metadata"></label>
        <button id="apply-cloud-device-filters" type="button">Apply filters</button>
      </div>
      <div class="support-actions" aria-label="Cloud sync control">
        <button class="primary" id="pause-cloud-sync" type="button" hidden>Pause cloud sync</button>
        <button class="primary" id="resume-cloud-sync" type="button" hidden>Resume cloud sync</button>
      </div>
      <div id="cloud-sync-confirm" role="group" aria-labelledby="cloud-sync-confirm-heading" hidden>
        <h4 id="cloud-sync-confirm-heading">Confirm cloud sync change</h4>
        <p id="cloud-sync-confirm-summary" class="note"></p>
        <form id="cloud-sync-form">
          <label>Type the affected account's full email
            <input id="cloud-sync-email" type="email" autocomplete="off" required></label>
          <label>Reason
            <select id="cloud-sync-reason"><option value="support_review">Support review</option>
              <option value="suspected_compromise">Suspected compromise</option></select></label>
          <label>Fresh code from your own authenticator app
            <input id="cloud-sync-otp" inputmode="numeric" autocomplete="one-time-code"
              pattern="[0-9]{6}" maxlength="6" required></label>
          <div class="support-actions"><button class="primary" type="submit">Confirm cloud sync change</button>
            <button id="cancel-cloud-sync" type="button">Cancel</button></div>
        </form>
      </div>
      <div id="cloud-device-state" role="status" aria-live="polite" tabindex="-1">Registered sessions have not been loaded.</div>
      <ul id="cloud-device-list" class="device-list"></ul>
    </section>
    <div id="device-revoke-panel" role="group" aria-labelledby="device-revoke-heading" hidden>
      <h4 id="device-revoke-heading">Confirm cloud session revocation</h4>
      <p id="device-revoke-summary" class="note"></p>
      <form id="device-revoke-form">
        <label>Type the affected account's full email
          <input id="device-revoke-email" type="email" autocomplete="off" required></label>
        <label>Fresh code from your own authenticator app
          <input id="device-revoke-otp" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength="6" required></label>
        <div class="support-actions"><button class="primary" type="submit">Revoke this cloud session</button>
          <button id="cancel-device-revoke" type="button">Cancel</button></div>
      </form>
    </div>
  </section>
  <div class="support-grid">
    <label>Support action
      <select id="support-kind">
        <option value="suspend_24h">Suspend cloud login for 24 hours</option>
        <option value="restore_login">Restore cloud login</option>
        <option value="session_review">Request session revocation review</option>
        <option value="mfa_review">Request 2FA recovery review</option>
        <option value="deletion_review">Request account deletion review</option>
      </select>
    </label>
    <label>Reason (no passwords or private data)
      <select id="support-reason">
        <option value="user_request">Verified user request</option>
        <option value="lost_device">Lost device</option>
        <option value="suspected_compromise">Suspected account compromise</option>
        <option value="access_problem">Access problem</option>
        <option value="policy_violation">Policy violation</option>
      </select>
    </label>
  </div>
  <div class="support-actions">
    <button class="primary" id="prepare-support" type="button">Prepare support action</button>
    <button id="close-support" type="button">Cancel</button>
  </div>
  <div id="support-confirm" hidden>
    <h2>Final confirmation</h2>
    <p id="support-summary" class="note"></p>
    <form id="support-form">
      <label>Type the affected account's full email to confirm
        <input id="support-email" type="email" autocomplete="off" required></label>
      <label>Fresh code from your own authenticator app
        <input id="support-otp" inputmode="numeric" autocomplete="one-time-code" pattern="[0-9]{6}" maxlength="6" required></label>
      <button class="primary" type="submit">Confirm cloud support action</button>
    </form>
  </div>
</section>
<section id="support-history-section" aria-labelledby="support-history-heading">
  <div class="toolbar"><h2 id="support-history-heading">Support activity</h2>
    <button id="refresh-history" type="button">Refresh history</button></div>
  <p class="note">Metadata-only audit history. No vault content, recovery codes or session tokens.</p>
  <div id="support-history-state" role="status" aria-live="polite"></div>
  <ul class="case-history" id="support-history"></ul>
</section>
<section id="security-center" aria-labelledby="security-center-heading">
  <div class="toolbar"><h2 id="security-center-heading">Security Center</h2>
    <button id="refresh-security-events" type="button">Refresh security activity</button></div>
  <p class="note">Last 90 days of privacy-limited first-party events: opt-in cloud session enrollment/revocation, support actions, recovery requests, and verified-user admin access denials. Password login failures, emails, IP addresses, tokens, and vault data are not collected here.</p>
  <div id="security-alerts" class="security-alerts" role="group" aria-label="Recent security signal counts"></div>
  <div id="security-events-state" role="status" aria-live="polite">Security activity has not been loaded.</div>
  <ul id="security-events" class="security-events"></ul>
</section>
<section><h2>About account recovery</h2><p>Send a password recovery email for a user's <strong>cloud login account</strong>.
The user chooses a new cloud password using their own verified email.
<strong>Master Passphrases cannot be reset by the administrator</strong>: private vault encryption remains independent.</p></section>
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
  await loadPage(1);await loadHistory();await loadSecurityEvents();
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
function statusCell(row,value,state){
  const cell=document.createElement("td"),badge=document.createElement("span");
  badge.className="status "+state;badge.textContent=value;cell.append(badge);row.append(cell);
}
let matching=0,loadSequence=0,searchDelay=null;
function display(){
  const body=get("users");body.replaceChildren();
  for(const user of accounts){
    const row=document.createElement("tr");
    td(row,user.email);
    td(row,date(user.createdAt));
    td(row,date(user.lastSignInAt));
    statusCell(row,user.confirmed?"Verified":"Pending",user.confirmed?"yes":"no");
    statusCell(row,user.mfa===true?"Enabled":user.mfa===false?"Off":"Unknown",
      user.mfa===true?"yes":user.mfa===false?"no":"unknown");
    const action=document.createElement("td"),btn=document.createElement("button");
    btn.type="button";
    btn.textContent=user.isSelf?"Reset my cloud password":"Send recovery email";
    btn.disabled=!user.confirmed;
    btn.addEventListener("click",()=>sendRecovery(user));
    action.append(btn);
    if(!user.isSelf){
      const manage=document.createElement("button");
      manage.type="button";manage.textContent="Manage";manage.style.marginLeft="8px";
      manage.setAttribute("aria-label","Manage cloud access and support for "+user.email);
      manage.addEventListener("click",()=>openSupport(user,manage));
      action.append(manage);
    }
    row.append(action);body.append(row);
  }
  visible("empty",accounts.length===0);
}
function setLoading(value){
  get("directory-table").setAttribute("aria-busy",String(value));
  get("prev").disabled=value||page<=1;
  get("next").disabled=value||page*30>=matching;
  get("directory-state").textContent=value?"Loading accounts…":"";
}
async function loadPage(n){
  if(deviceRevocationBusy)return;
  const seq=++loadSequence;
  setLoading(true);
  try{
    const data=await api("list",{
      page:n,search:get("search").value.trim(),
      emailState:get("email-filter").value,
      mfaState:get("mfa-filter").value,
    });
    if(seq!==loadSequence||!accessToken)return;
    if(!Array.isArray(data.users)||typeof data.total!=="number"||
       typeof data.matching!=="number"||!data.stats)throw Error("Invalid directory response");
    page=n;accounts=data.users;matching=data.matching;
    get("total").textContent=data.total.toLocaleString();
    get("verified-stat").textContent=data.stats.verified.toLocaleString();
    get("mfa-stat").textContent=data.stats.mfaEnabled.toLocaleString();
    get("active-stat").textContent=data.stats.active30Days.toLocaleString();
    get("filtered-count").textContent=matching.toLocaleString()+" matching / "+
      data.total.toLocaleString()+" total";
    const pageCount=Math.max(1,Math.ceil(matching/30));
    get("page-label").textContent="Page "+page+" of "+pageCount;
    visible("retry-directory",false);display();alertText("");
  }catch(error){
    if(seq!==loadSequence)return;
    accounts=[];matching=0;
    get("users").replaceChildren();visible("empty",false);
    get("directory-state").textContent="Unable to load accounts. Retry or adjust filters.";
    visible("retry-directory",true);alertText(error.message,true);
  }finally{
    if(seq===loadSequence){
      const state=get("directory-state").textContent;
      setLoading(false);
      if(state.startsWith("Unable"))get("directory-state").textContent=state;
    }
  }
}
function scheduleSearch(){
  clearTimeout(searchDelay);
  searchDelay=setTimeout(()=>{if(accessToken)loadPage(1)},350);
}
async function sendRecovery(user){
  const message=user.isSelf
    ? "Send yourself a cloud account password reset email? Your encrypted vault master passphrase will not change."
    : "Send a cloud account password recovery email to "+user.email+"? This will not change their master passphrase.";
  if(!confirm(message))return;
  try{await api("recover",{id:user.id});alertText("Recovery email requested for "+user.email+". Check the inbox and spam folder.");}
  catch(error){alertText(error.message,true);}
}
get("retry-directory").addEventListener("click",()=>{if(accessToken)loadPage(page)});
let supportTarget=null,pendingSupport=null,supportTrigger=null;
async function loadSupportStatus(){
  if(!supportTarget)return;
  const id=supportTarget.id;
  const state=get("support-cloud-state");
  state.textContent="Checking server-side cloud login status…";
  try{
    const data=await api("supportStatus",{id});
    if(!supportTarget||supportTarget.id!==id)return;
    const item=data.cloudLogin||{};
    state.textContent=item.state==="suspended"
      ? "Cloud sign-in suspended until "+new Date(item.until).toLocaleString()+". Existing JWTs may remain valid; offline vaults are unaffected."
      : item.state==="active"
      ? "Cloud login currently active. This does not indicate that all device sessions have signed out."
      : "Cloud login status could not be determined. Do not assume the account is active.";
  }catch(error){
    if(supportTarget&&supportTarget.id===id)
      state.textContent="Could not verify cloud login status: "+error.message;
  }
}
get("refresh-support-status").addEventListener("click",loadSupportStatus);
let pendingDevice=null,pendingDeviceTrigger=null,deviceRevocationBusy=false,cloudSyncBusy=false,pendingSyncSuspend=null,pendingSyncTrigger=null,cloudDeviceLoadSequence=0;
function clearDeviceConfirmation({restoreFocus=false,force=false}={}){
  if(deviceRevocationBusy&&!force)return;
  const trigger=pendingDeviceTrigger;
  pendingDevice=null;pendingDeviceTrigger=null;
  get("device-revoke-email").value="";
  get("device-revoke-otp").value="";
  visible("device-revoke-panel",false);
  if(restoreFocus){
    if(trigger?.isConnected)trigger.focus();
    else get("cloud-device-heading").focus();
  }
}
function setDeviceRevocationBusy(busy){
  deviceRevocationBusy=busy;
  const panel=get("device-revoke-panel"),form=get("device-revoke-form");
  panel.setAttribute("aria-busy",String(busy));
  for(const control of form.querySelectorAll("input,button"))control.disabled=busy;
  get("refresh-cloud-devices").disabled=busy;
  get("apply-cloud-device-filters").disabled=busy;
  get("pause-cloud-sync").disabled=busy;
  get("resume-cloud-sync").disabled=busy;
  get("close-support").disabled=busy;
  get("logout").disabled=busy;
  for(const control of get("cloud-device-list").querySelectorAll("button"))control.disabled=busy;
  if(busy)get("cloud-device-state").textContent="Revocation is in progress. Keep this page open for the result.";
}
const SYNC_OUTCOME_LABELS={success:"Successful",failure:"Failed",unknown:"Not reported"};
const SYNC_ERROR_LABELS={quota_exceeded:"Storage limit reached",sync_suspended:"Sync paused",
  network:"Network issue",auth:"Sign-in required",transport:"Cloud service issue",unknown:"Other issue"};
function safeBytes(value){return Number.isSafeInteger(value)&&value>=0?value:null;}
function formatBytes(value){
  if(value<1024)return value+" bytes";
  return (value/(1024*1024)).toFixed(2)+" MiB";
}
function dateTime(value){
  return typeof value==="string"&&Number.isFinite(Date.parse(value))
    ?new Date(value).toLocaleString():"—";
}
function renderCloudDevices(devices){
  const list=get("cloud-device-list");list.replaceChildren();
  for(const item of devices){
    const li=document.createElement("li");
    const title=document.createElement("h4");
    title.textContent=item.display_name||"Unnamed app session";
    li.append(title);
    const platform=document.createElement("p");
    platform.textContent=[item.platform,item.os_version?"OS "+item.os_version:"",item.app_version?"App "+item.app_version:""]
      .filter(Boolean).join(" · ")||"Platform details not reported";
    li.append(platform);
    const dates=document.createElement("p");
    dates.textContent="Registered "+dateTime(item.registered_at)+
      " · Last successful sync "+dateTime(item.last_sync_at);
    li.append(dates);
    const activity=document.createElement("p");
    const outcome=Object.hasOwn(SYNC_OUTCOME_LABELS,item.last_sync_outcome)
      ?SYNC_OUTCOME_LABELS[item.last_sync_outcome]:"Not reported";
    const bytes=safeBytes(item.last_sync_bytes);
    const error=Object.hasOwn(SYNC_ERROR_LABELS,item.last_sync_error_code)
      ?SYNC_ERROR_LABELS[item.last_sync_error_code]:"";
    activity.textContent="Last attempt "+dateTime(item.last_sync_attempt_at)+" · "+outcome+
      (bytes===null?"":" · "+formatBytes(bytes))+(error?" · "+error:"");
    li.append(activity);
    const state=document.createElement("p");
    state.textContent=item.revoked_at
      ?"Revoked "+dateTime(item.revoked_at)
      :"Cloud session not revoked by Lockvyn";
    li.append(state);
    if(!item.revoked_at){
      const button=document.createElement("button");
      button.type="button";button.textContent="Revoke cloud access for this session";
      button.setAttribute("aria-label","Revoke cloud access for "+
        (item.display_name||"unnamed app session")+" for "+supportTarget.email);
      button.disabled=deviceRevocationBusy||cloudSyncBusy;
      button.addEventListener("click",()=>{
        if(deviceRevocationBusy||cloudSyncBusy)return;
        pendingDevice=item;pendingDeviceTrigger=button;
        get("device-revoke-summary").textContent=
          "This blocks "+(item.display_name||"this app session")+
          " from cloud snapshots and pairing for "+supportTarget.email+
          ". Offline data on the app remains.";
        get("device-revoke-email").value="";
        get("device-revoke-otp").value="";
        visible("device-revoke-panel",true);
        get("device-revoke-email").focus();
      });
      li.append(button);
    }
    list.append(li);
  }
  if(!devices.length){
    const empty=document.createElement("li");
    empty.textContent="No registered sessions match these filters.";
    list.append(empty);
  }
}
async function loadCloudDevices(){
  if(!supportTarget)return;
  const id=supportTarget.id,state=get("cloud-device-state"),sequence=++cloudDeviceLoadSequence;
  state.textContent="Loading registered cloud sessions and sync activity…";
  try{
    const selectedSince=get("cloud-device-since").value;
    const data=await api("deviceList",{
      id,
      platform:get("cloud-device-platform").value||null,
      outcome:get("cloud-device-outcome").value,
      sinceDays:selectedSince==="all"?null:Number(selectedSince),
      deviceQuery:get("cloud-device-query").value.trim()||null,
    });
    if(sequence!==cloudDeviceLoadSequence||!supportTarget||supportTarget.id!==id)return;
    if(!Array.isArray(data.devices)||typeof data.suspended!=="boolean")throw Error("Invalid sync inventory response");
    for(const key of ["snapshotCount","ciphertextBytes","quotaBytes","remainingBytes"])
      if(!Number.isSafeInteger(data[key])||data[key]<0)throw Error("Invalid sync usage response");
    renderCloudDevices(data.devices);
    get("cloud-sync-usage").textContent="Encrypted cloud snapshots: "+data.snapshotCount+
      " of "+data.maxSnapshots+" · "+formatBytes(data.ciphertextBytes)+" used of "+
      formatBytes(data.quotaBytes)+" · "+formatBytes(data.remainingBytes)+" remaining";
    get("cloud-sync-state").textContent=data.suspended
      ?"Cloud sync is paused. Offline vault data and encrypted cloud snapshots are preserved."
      :"Cloud sync is active. Pausing it will preserve offline vault data and encrypted cloud snapshots.";
    visible("pause-cloud-sync",!data.suspended);
    visible("resume-cloud-sync",data.suspended);
    state.textContent=data.devices.length
      ?data.devices.length+" registered session"+(data.devices.length===1?"":"s")+" match these filters."
      :"No registered sessions match these filters.";
  }catch(error){
    if(sequence===cloudDeviceLoadSequence&&supportTarget&&supportTarget.id===id){
      state.textContent="Could not load cloud sync activity: "+error.message;
      get("cloud-sync-usage").textContent="Sync usage is unavailable.";
      get("cloud-sync-state").textContent="Cloud sync state is unavailable.";
    }
  }
}
get("refresh-cloud-devices").addEventListener("click",loadCloudDevices);
get("apply-cloud-device-filters").addEventListener("click",loadCloudDevices);
get("cancel-device-revoke").addEventListener("click",()=>clearDeviceConfirmation({restoreFocus:true}));
get("device-revoke-form").addEventListener("submit",async e=>{
  e.preventDefault();
  if(deviceRevocationBusy)return;
  const selected=pendingDevice,target=supportTarget;
  if(!selected||!target){alertText("Select an active registered session first.",true);return;}
  const email=get("device-revoke-email").value.trim();
  if(email.toLowerCase()!==target.email.toLowerCase()){
    alertText("The confirmation email does not match.",true);return;
  }
  if(!confirm("Revoke cloud access for this app session? Offline vault data stays on the app."))return;
  setDeviceRevocationBusy(true);
  try{
    const user=await authFetch("user",null,"GET",accessToken);
    const factor=(Array.isArray(user.factors)?user.factors:[])
      .find(x=>x.factor_type==="totp"&&x.status==="verified");
    if(!factor)throw Error("No verified administrator authenticator factor.");
    const challenge=await authFetch("factors/"+encodeURIComponent(factor.id)+"/challenge",{},"POST",accessToken);
    const fresh=await authFetch("factors/"+encodeURIComponent(factor.id)+"/verify",{
      challenge_id:challenge.id,code:get("device-revoke-otp").value.trim(),
    },"POST",accessToken);
    get("device-revoke-otp").value="";
    if(jwtAal(fresh.access_token)!=="aal2")throw Error("Fresh MFA verification did not succeed.");
    accessToken=fresh.access_token;
    await api("deviceRevoke",{id:target.id,deviceId:selected.device_id,confirmEmail:email});
    clearDeviceConfirmation({force:true});
    await loadCloudDevices();
    get("cloud-device-state").focus();
    alertText("Cloud access revoked for that signed-in app session. Offline vault data remains.");
  }catch(error){
    get("device-revoke-otp").value="";
    alertText(error.message,true);
  }finally{setDeviceRevocationBusy(false);}
});
function setCloudSyncBusy(busy){
  cloudSyncBusy=busy;
  const form=get("cloud-sync-form");
  get("cloud-sync-confirm").setAttribute("aria-busy",String(busy));
  for(const control of form.querySelectorAll("input,select,button"))control.disabled=busy;
  get("refresh-cloud-devices").disabled=busy;
  get("apply-cloud-device-filters").disabled=busy;
  get("pause-cloud-sync").disabled=busy;
  get("resume-cloud-sync").disabled=busy;
  get("close-support").disabled=busy;
  get("logout").disabled=busy;
  for(const control of get("cloud-device-list").querySelectorAll("button"))control.disabled=busy;
  if(busy)get("cloud-sync-state").textContent="Saving cloud sync state. Keep this page open for the result.";
}
function openCloudSyncControl(suspend,trigger){
  if(!supportTarget||deviceRevocationBusy||cloudSyncBusy)return;
  pendingSyncSuspend=suspend;pendingSyncTrigger=trigger;
  get("cloud-sync-confirm-summary").textContent=(suspend
    ?"Pause encrypted cloud snapshot sync for "
    :"Resume encrypted cloud snapshot sync for ")+supportTarget.email+
    "? Offline vault data and encrypted cloud snapshots will be preserved. A fresh authenticator code and typed email are required.";
  get("cloud-sync-email").value="";get("cloud-sync-otp").value="";
  visible("cloud-sync-confirm",true);get("cloud-sync-email").focus();
}
function closeCloudSyncConfirmation(restoreFocus=false){
  if(cloudSyncBusy)return;
  const trigger=pendingSyncTrigger;
  pendingSyncSuspend=null;pendingSyncTrigger=null;
  get("cloud-sync-email").value="";get("cloud-sync-otp").value="";
  visible("cloud-sync-confirm",false);
  if(restoreFocus&&trigger?.isConnected)trigger.focus();
}
get("pause-cloud-sync").addEventListener("click",e=>openCloudSyncControl(true,e.currentTarget));
get("resume-cloud-sync").addEventListener("click",e=>openCloudSyncControl(false,e.currentTarget));
get("cancel-cloud-sync").addEventListener("click",()=>closeCloudSyncConfirmation(true));
get("cloud-sync-form").addEventListener("submit",async e=>{
  e.preventDefault();
  if(pendingSyncSuspend===null||!supportTarget||deviceRevocationBusy||cloudSyncBusy)return;
  const suspend=pendingSyncSuspend,target=supportTarget,email=get("cloud-sync-email").value.trim();
  if(email.toLowerCase()!==target.email.toLowerCase()){
    alertText("The confirmation email does not match.",true);return;
  }
  if(!confirm((suspend?"Pause":"Resume")+" cloud sync for "+target.email+
      "? Offline vault data and encrypted cloud snapshots are preserved."))return;
  const button=get("cloud-sync-form").querySelector('button[type="submit"]');
  button.disabled=true;setCloudSyncBusy(true);
  try{
    const user=await authFetch("user",null,"GET",accessToken);
    const factor=(Array.isArray(user.factors)?user.factors:[])
      .find(x=>x.factor_type==="totp"&&x.status==="verified");
    if(!factor)throw Error("No verified administrator authenticator factor.");
    const challenge=await authFetch("factors/"+encodeURIComponent(factor.id)+"/challenge",{},"POST",accessToken);
    const fresh=await authFetch("factors/"+encodeURIComponent(factor.id)+"/verify",{
      challenge_id:challenge.id,code:get("cloud-sync-otp").value.trim(),
    },"POST",accessToken);
    get("cloud-sync-otp").value="";
    if(jwtAal(fresh.access_token)!=="aal2")throw Error("Fresh MFA verification did not succeed.");
    accessToken=fresh.access_token;
    const done=await api("syncControl",{
      id:target.id,suspend,reasonCode:get("cloud-sync-reason").value,confirmEmail:email,
    });
    pendingSyncSuspend=null;pendingSyncTrigger=null;
    visible("cloud-sync-confirm",false);
    setCloudSyncBusy(false);
    await loadCloudDevices();
    get("cloud-sync-state").focus();
    alertText(done.warning);
  }catch(error){
    get("cloud-sync-otp").value="";
    alertText(error.message,true);
  }finally{
    setCloudSyncBusy(false);button.disabled=false;
  }
});
function openSupport(user,trigger=null){
  if(deviceRevocationBusy||cloudSyncBusy){alertText("A cloud control is still being processed. Wait for its result.",true);return;}
  closeCloudSyncConfirmation();
  supportTrigger=trigger;
  supportTarget={id:user.id,email:user.email,isSelf:user.isSelf};
  pendingSupport=null;visible("support-confirm",false);
  get("support-target").textContent="Selected: "+user.email;
  visible("account-support",true);
  void loadSupportStatus();
  clearDeviceConfirmation();
  get("cloud-device-list").replaceChildren();
  get("cloud-device-state").textContent="Loading registered cloud sessions…";
  void loadCloudDevices();
  get("account-support").scrollIntoView({block:"start",behavior:"instant"});
  get("support-heading").focus();
}
function closeSupport(){
  if(deviceRevocationBusy||cloudSyncBusy){alertText("A cloud control is still being processed. Wait for its result.",true);return;}
  const trigger=supportTrigger;
  closeCloudSyncConfirmation();
  supportTrigger=null;
  supportTarget=null;pendingSupport=null;
  get("support-email").value="";get("support-otp").value="";
  clearDeviceConfirmation();get("cloud-device-list").replaceChildren();
  get("cloud-device-state").textContent="Registered sessions have not been loaded.";
  visible("support-confirm",false);visible("account-support",false);
  get("support-cloud-state").textContent="Cloud login status has not been checked.";
  if(trigger?.isConnected)trigger.focus();
  else get("directory-heading").focus();
}
get("close-support").addEventListener("click",closeSupport);
get("support-kind").addEventListener("change",()=>{pendingSupport=null;visible("support-confirm",false)});
get("support-reason").addEventListener("change",()=>{pendingSupport=null;visible("support-confirm",false)});
get("prepare-support").addEventListener("click",async()=>{
  if(!supportTarget||supportTarget.isSelf)return;
  const kind=get("support-kind").value,reason=get("support-reason").value;
  try{
    const prepared=await api("supportPrepare",{id:supportTarget.id,kind,reason});
    pendingSupport={caseId:prepared.caseId,id:supportTarget.id,kind,reason,email:prepared.targetEmail};
    get("support-summary").textContent="Action: "+kind+". Confirm exactly "+
      prepared.targetEmail+". This case expires in 5 minutes. No action occurs until confirmation.";
    visible("support-confirm",true);
    get("support-email").value="";get("support-otp").value="";
    alertText("Case prepared. Reconfirm target email and your authenticator code.");
  }catch(e){alertText(e.message,true);}
});
get("support-form").addEventListener("submit",async e=>{
  e.preventDefault();
  const selected=pendingSupport;
  if(!selected){alertText("Prepare a new support case first.",true);return;}
  if(get("support-email").value.trim().toLowerCase()!==selected.email.toLowerCase()){
    alertText("The confirmation email does not match.",true);return;
  }
  if(!confirm("Confirm "+selected.kind+" for "+selected.email+"? This affects CLOUD access only."))return;
  const confirmBtn=get("support-form").querySelector('button[type="submit"]');
  confirmBtn.disabled=true;
  try{
    const user=await authFetch("user",null,"GET",accessToken);
    const factor=(Array.isArray(user.factors)?user.factors:[])
      .find(x=>x.factor_type==="totp"&&x.status==="verified");
    if(!factor)throw Error("No verified administrator authenticator factor.");
    const challenge=await authFetch("factors/"+encodeURIComponent(factor.id)+"/challenge",{},"POST",accessToken);
    const fresh=await authFetch("factors/"+encodeURIComponent(factor.id)+"/verify",{
      challenge_id:challenge.id,code:get("support-otp").value.trim(),
    },"POST",accessToken);
    get("support-otp").value="";
    if(jwtAal(fresh.access_token)!=="aal2")throw Error("Fresh MFA verification did not succeed.");
    accessToken=fresh.access_token;
    const done=await api("supportExecute",{
      id:selected.id,kind:selected.kind,reason:selected.reason,
      caseId:selected.caseId,confirmEmail:get("support-email").value.trim(),
    });
    await loadSupportStatus();await loadPage(page);await loadHistory();
    closeSupport();
    alertText("Support case recorded: "+done.status+". "+done.warning);
  }catch(error){get("support-otp").value="";alertText(error.message,true);}
  finally{confirmBtn.disabled=false;}
});
const SECURITY_EVENT_LABELS={
  account_recovery_requested:"Cloud password recovery requested",
  cloud_device_unregistered:"Cloud session inventory consent withdrawn",
  cloud_session_registered:"New opt-in cloud session registered",
  cloud_session_revoked:"Cloud session access revoked",
  cloud_sync_suspended:"Encrypted cloud sync paused",
  cloud_sync_resumed:"Encrypted cloud sync resumed",
  support_case_opened:"Support case opened",
  support_action_completed:"Cloud support action completed",
  support_action_failed:"Cloud support action failed",
  support_review_pending:"Support action sent for review",
  admin_step_up_denied:"Admin access blocked: verified 2FA required",
  admin_allowlist_denied:"Admin access blocked: account is not authorized",
  admin_inactive_session:"Admin access blocked: Auth session is inactive"
};
function safeCount(value){return Number.isSafeInteger(value)&&value>=0?value:0;}
function shortId(value){return typeof value==="string"&&/^[a-f0-9-]{36}$/i.test(value)?value.slice(0,8):"";}
function renderSecurityEvents(data){
  if(!data||!Array.isArray(data.events)||!data.alerts)throw Error("Invalid security activity response");
  const alerts=data.alerts,host=get("security-alerts");host.replaceChildren();
  const repeated=safeCount(alerts.repeatedDeniedActors15m);
  const cards=[
    ["Repeated denied actors · 15 min",repeated,repeated?"Review repeated admin-access denials.":"No repeated actor reached the alert threshold.",repeated>0],
    ["2FA step-up denials · 15 min",safeCount(alerts.adminStepUpDenials15m),"Valid accounts blocked before admin access.",false],
    ["New opt-in sessions · 24 h",safeCount(alerts.newCloudSessions24h),"Session registrations reported by opted-in clients.",false],
    ["Failed support actions · 24 h",safeCount(alerts.failedSupportActions24h),"Review the corresponding support history.",false]
  ];
  for(const [label,count,hint,warning] of cards){
    const card=document.createElement("article");card.className="security-alert"+(warning?" warning":"");
    const title=document.createElement("small");title.textContent=label;
    const value=document.createElement("strong");value.textContent=String(count);
    const note=document.createElement("small");note.textContent=hint;
    card.append(title,value,note);host.append(card);
  }
  const list=get("security-events");list.replaceChildren();
  for(const item of data.events){
    const row=document.createElement("li");
    const title=document.createElement("strong");
    title.textContent=Object.hasOwn(SECURITY_EVENT_LABELS,item.eventType)?SECURITY_EVENT_LABELS[item.eventType]:"Security activity";
    const meta=document.createElement("p");
    const actor=shortId(item.actorId),subject=shortId(item.subjectId);
    const time=typeof item.occurredAt==="string"&&Number.isFinite(Date.parse(item.occurredAt))
      ?new Date(item.occurredAt).toLocaleString():"Unknown time";
    const details=[];
    if(actor)details.push("actor "+actor);
    if(subject)details.push("account "+subject);
    if(typeof item.detailCode==="string")details.push(item.detailCode.replaceAll("_"," "));
    details.push(time);
    meta.textContent=details.join(" · ");
    row.append(title,meta);list.append(row);
  }
  get("security-events-state").textContent=data.events.length
    ?"Showing "+data.events.length+" recent metadata-only security events."
    :"No retained security events yet.";
}
async function loadSecurityEvents(){
  if(!accessToken)return;
  const state=get("security-events-state");
  state.textContent="Loading security activity…";
  try{
    const data=await api("securityEvents",{limit:50});
    renderSecurityEvents(data);
  }catch(error){
    get("security-events").replaceChildren();
    get("security-alerts").replaceChildren();
    state.textContent="Could not load security activity.";
    alertText(error.message,true);
  }
}
get("refresh-security-events").addEventListener("click",()=>{if(accessToken)loadSecurityEvents()});
async function loadHistory(){
  const info=get("support-history-state");
  info.textContent="Loading support cases…";
  try{
    const data=await api("supportHistory",{});
    const list=get("support-history");list.replaceChildren();
    for(const item of data.cases){
      const li=document.createElement("li");
      li.textContent=item.kind+" · "+item.state+" · "+item.reason+" · "+
        date(item.createdAt)+" · target "+item.targetId;
      list.append(li);
    }
    info.textContent=data.cases.length?"Latest 25 cases":"No support cases yet.";
  }catch(e){info.textContent="Could not load support cases.";alertText(e.message,true);}
}
get("refresh-history").addEventListener("click",()=>{if(accessToken)loadHistory()});
get("search").addEventListener("input",scheduleSearch);
get("email-filter").addEventListener("change",()=>{if(accessToken)loadPage(1)});
get("mfa-filter").addEventListener("change",()=>{if(accessToken)loadPage(1)});
get("clear-filters").addEventListener("click",()=>{
  get("search").value="";get("email-filter").value="all";
  get("mfa-filter").value="all";if(accessToken)loadPage(1);
});
get("prev").addEventListener("click",()=>{if(page>1&&!get("prev").disabled)loadPage(page-1);});
get("next").addEventListener("click",()=>{if(!get("next").disabled)loadPage(page+1);});
get("logout").addEventListener("click",()=>{
  if(deviceRevocationBusy||cloudSyncBusy){alertText("A cloud control is still being processed. Wait for its result.",true);return;}
  ++loadSequence;clearTimeout(searchDelay);
  accessToken="";accounts=[];matching=0;closeSupport();
  get("support-history").replaceChildren();
  visible("dashboard",false);visible("login",true);
  visible("logout",false);alertText("Signed out.");get("email").focus();
});
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
