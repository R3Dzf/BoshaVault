// User-owned cloud security action. This page never accepts administrator privileges.
// Cloud password, TOTP and tokens go directly to Supabase Auth and remain in memory only.
export function securityHtml(n: string, publicKey: string, apiUrl: string): string {
  const safe = (v: string) => JSON.stringify(v).replace(/</g, "\\u003c")
    .replace(/>/g, "\\u003e").replace(/&/g, "\\u0026");
  return `<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Lockvyn · Sign out all cloud sessions</title>
<style nonce="${n}">
:root{color-scheme:light;font-family:system-ui,-apple-system,"Segoe UI",sans-serif;background:#f6f5fb;color:#252039}
*{box-sizing:border-box}body{margin:0}main{max-width:620px;margin:0 auto;padding:38px 18px 80px}
section{border:1px solid #e2deed;border-radius:16px;background:white;padding:27px;box-shadow:0 4px 20px #19103309}
h1{font-size:26px;margin:0 0 12px}h2{font-size:18px;margin:0 0 12px}
p,li{line-height:1.65;color:#57506e}label{display:grid;gap:7px;font-weight:650;font-size:14px}
form{display:grid;gap:15px}input{font:inherit;width:100%;min-height:44px;padding:10px;border-radius:10px;border:1px solid #bcb5d2}
button{font:inherit;font-weight:700;border:1px solid #5e50c0;color:#fff;background:#5e50c0;border-radius:10px;padding:12px;cursor:pointer;min-height:44px}
button:disabled{opacity:.5;cursor:not-allowed}button:focus-visible,input:focus-visible,a:focus-visible{outline:3px solid #8d7cff;outline-offset:2px}
#message{min-height:24px;margin:16px 0;font-weight:650}#message.error{color:#ab2537}
#message.success{color:#126848}#confirm-box{border:1px solid #ecddd0;padding:16px;border-radius:11px}
[hidden]{display:none!important}@media(max-width:450px){section{padding:18px}}@media(prefers-reduced-motion:reduce){*{transition:none!important}}
</style>
</head>
<body><main><a href="/">Back to Lockvyn administration</a>
<section>
<h1>Sign out all my cloud sessions</h1>
<p>This is a <strong>self-service</strong> security action for your own Lockvyn account, not for another user's account. Only you can approve it by signing in.</p>
<p>All Supabase cloud refresh tokens for your account are revoked. Already issued access tokens can work until expiry; encrypted vaults stored offline are not erased, disabled or decrypted.</p>
<div id="message" aria-live="polite" role="status"></div>
<form id="sign-in">
<label>My cloud email<input id="email" type="email" autocomplete="username" required maxlength="320"></label>
<label>My cloud password<input id="password" type="password" autocomplete="current-password" required></label>
<button type="submit">Verify my cloud account</button>
</form>
<form id="verify-otp" hidden>
<h2>Authenticator confirmation</h2>
<label>Code from my verified authenticator<input id="otp" inputmode="numeric" pattern="[0-9]{6}" maxlength="6" autocomplete="one-time-code" required></label>
<button type="submit">Verify authenticator</button>
</form>
<div id="confirm-box" hidden>
<h2>Final confirmation</h2>
<p>Confirm that you want to revoke all <strong>your own</strong> active cloud refresh tokens. Other devices will have to sign in again once their current access tokens expire. This cannot lock or delete an offline encrypted vault.</p>
<button id="revoke" type="button">Yes, sign out my cloud sessions</button>
</div>
</section>
</main>
<script nonce="${n}">
"use strict";
const BASE=${safe(apiUrl)},PUBLIC_KEY=${safe(publicKey)};
const el=id=>document.getElementById(id);
let accessToken="",factorId="",challengeId="",actorId="";
function notice(text,bad=false){const box=el("message");box.textContent=text;box.className=bad?"error":"success";}
function visible(id,value){el(id).hidden=!value;}
function reset(){accessToken="";factorId="";challengeId="";actorId="";el("password").value="";el("otp").value="";}
async function auth(endpoint,payload=null,method="POST",jwt=""){
  const response=await fetch(BASE+"/auth/v1/"+endpoint,{
    method,cache:"no-store",credentials:"omit",referrerPolicy:"no-referrer",
    headers:{"apikey":PUBLIC_KEY,"Content-Type":"application/json",
      ...(jwt?{"Authorization":"Bearer "+jwt}:{})},
    body:payload===null?undefined:JSON.stringify(payload)
  });
  const text=await response.text();
  let data={};if(text){try{data=JSON.parse(text)}catch{throw Error("Unexpected Auth response");}}
  if(!response.ok)throw Error(data.msg||data.message||data.error_description||"Cloud Auth refused the request");
  return data;
}
el("sign-in").addEventListener("submit",async e=>{
  e.preventDefault();reset();notice("Checking your cloud account…");
  const button=el("sign-in").querySelector("button");button.disabled=true;
  try{
    const out=await auth("token?grant_type=password",{email:el("email").value.trim(),password:el("password").value});
    accessToken=out.access_token;actorId=out.user?.id;
    el("password").value="";
    if(typeof accessToken!=="string"||typeof actorId!=="string")throw Error("Invalid sign-in result");
    const me=await auth("user",null,"GET",accessToken);
    if(me.id!==actorId)throw Error("Cloud identity changed");
    const factors=Array.isArray(me.factors)?me.factors:[];
    const verified=factors.filter(x=>x.status==="verified");
    if(verified.length){
      const factor=verified.find(x=>x.factor_type==="totp");
      if(!factor)throw Error("This account needs a supported verified authenticator method.");
      factorId=factor.id;
      const challenge=await auth("factors/"+encodeURIComponent(factorId)+"/challenge",{}, "POST",accessToken);
      challengeId=challenge.id;
      if(typeof challengeId!=="string")throw Error("Could not start authenticator verification");
      visible("sign-in",false);visible("verify-otp",true);
      notice("Confirm your existing authenticator. No sessions have been revoked.");
    }else{
      visible("sign-in",false);visible("confirm-box",true);
      notice("Cloud account verified. Confirm the logout below.");
    }
  }catch(error){reset();notice(error.message,true)}
  finally{button.disabled=false;el("password").value="";}
});
el("verify-otp").addEventListener("submit",async e=>{
  e.preventDefault();
  const button=el("verify-otp").querySelector("button");button.disabled=true;
  try{
    const reply=await auth("factors/"+encodeURIComponent(factorId)+"/verify",
      {challenge_id:challengeId,code:el("otp").value.trim()},"POST",accessToken);
    el("otp").value="";
    if(typeof reply.access_token!=="string")throw Error("Authenticator verification failed");
    const me=await auth("user",null,"GET",reply.access_token);
    if(me.id!==actorId)throw Error("Account mismatch");
    accessToken=reply.access_token;
    visible("verify-otp",false);visible("confirm-box",true);
    notice("Authenticator verified. Final consent is required.");
  }catch(error){notice(error.message,true);el("otp").value="";}
  finally{button.disabled=false;}
});
el("revoke").addEventListener("click",async()=>{
  if(!accessToken||!actorId)return;
  if(!confirm("Revoke all cloud refresh sessions for my own account?"))return;
  const button=el("revoke");button.disabled=true;
  try{
    await auth("logout?scope=global",null,"POST",accessToken);
    reset();visible("confirm-box",false);visible("sign-in",true);
    notice("Cloud refresh sessions revoked. Existing access tokens may work until expiry. Your offline vaults remain untouched.");
  }catch(error){notice("Cloud logout was not confirmed: "+error.message,true)}
  finally{button.disabled=false;}
});
</script></body></html>`;
}
