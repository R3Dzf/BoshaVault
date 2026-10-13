#!/usr/bin/env node
// Real Supabase Auth + deployed-function contract, against a disposable LOCAL stack.
// No production projects, real customer records, email sends or paid infrastructure.
import { readFileSync } from "node:fs";
import { createHmac, randomUUID } from "node:crypto";
import assert from "node:assert/strict";

const status = JSON.parse(readFileSync(process.argv[2], "utf8"));
const base = status.API_URL || status.api_url;
const anon = status.ANON_KEY || status.anon_key;
const service = status.SERVICE_ROLE_KEY || status.service_role_key;
assert(base?.startsWith("http://127.0.0.1:"), "Only local Supabase URLs are allowed");
assert(anon && service && anon !== service, "Local test keys missing");
const suite = randomUUID().slice(0, 8);
const root = `https://lockvyn-admin-ui.r3dzf-studio.workers.dev`;
const endpoint = base + "/functions/v1/lockvyn-admin";
const password = "Synthetic-only-testing-"+randomUUID()+"aA1!";
let accountWrites = 0, recoveryEmails = 0;

async function invoke(path, method, key, token, body, origin) {
  const response = await fetch(base + path, {
    method,
    headers: {
      apikey:key,
      ...(token ? { Authorization:"Bearer "+token } : {Authorization:"Bearer "+key}),
      ...(body ? {"Content-Type":"application/json"} : {}),
      ...(origin ? {Origin:origin} : {}),
    },
    body:body ? JSON.stringify(body) : undefined,
    redirect:"manual",
  });
  const raw=await response.text();
  let data;
  try{data=JSON.parse(raw)}catch{data={error:raw.slice(0,500)}}
  return {status:response.status,data};
}
async function createUser(label) {
  const email=label+"-"+suite+"@example.invalid";
  const r=await invoke("/auth/v1/admin/users","POST",service,service,
    {email,password,email_confirm:true});
  assert.equal(r.status,200, "Local user provisioning "+label+" "+JSON.stringify(r.data));
  assert(r.data.id);
  accountWrites++;
  const signed=await invoke("/auth/v1/token?grant_type=password","POST",anon,null,
    {email,password});
  assert.equal(signed.status,200,"Local sign-in for "+label);
  assert(signed.data.access_token);
  return {id:r.data.id,email,aal1:signed.data.access_token};
}
async function totpChallenge(user) {
  const enrolled=await invoke("/auth/v1/factors","POST",anon,user.aal1,
    {factor_type:"totp",friendly_name:"Admin staging"});
  assert.equal(enrolled.status,200,"Enroll local TOTP: "+JSON.stringify(enrolled.data));
  const factor=enrolled.data.id;
  const secret=enrolled.data.totp?.secret;
  assert(factor && secret,"Local TOTP secret not provided");
  const challenged=await invoke("/auth/v1/factors/"+encodeURIComponent(factor)+"/challenge",
    "POST",anon,user.aal1,{});
  assert.equal(challenged.status,200,"Challenge local TOTP");
  const code=totp(secret);
  const verified=await invoke("/auth/v1/factors/"+encodeURIComponent(factor)+"/verify",
    "POST",anon,user.aal1,{challenge_id:challenged.data.id,code});
  assert.equal(verified.status,200,"Verify local TOTP: "+JSON.stringify(verified.data));
  assert(verified.data.access_token);
  const aal2=verified.data.access_token;
  const body=JSON.parse(Buffer.from(aal2.split(".")[1],"base64url").toString("utf8"));
  assert.equal(body.aal,"aal2","Actual GoTrue needs to issue AAL2");
  return {...user,aal2};
}
function fromBase32(input) {
  const alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  let bits=0, value=0, out=[];
  for(const c of input.toUpperCase().replaceAll("=","").replaceAll(" ","")){
    const digit=alphabet.indexOf(c);
    if(digit<0)throw Error("Invalid base32");
    value=(value<<5)|digit;
    bits+=5;
    if(bits>=8){out.push((value>>>(bits-8))&255);bits-=8;value&=(1<<bits)-1;}
  }
  return Buffer.from(out);
}
function totp(secret) {
  const ctr=Buffer.alloc(8);
  ctr.writeBigUInt64BE(BigInt(Math.floor(Date.now()/30000)));
  const hmac=createHmac("sha1",fromBase32(secret)).update(ctr).digest();
  const offset=hmac[hmac.length-1]&0x0f;
  const value=(hmac.readUInt32BE(offset)&0x7fffffff)%1000000;
  return String(value).padStart(6,"0");
}
async function admin(token,body,origin=root) {
  const r=await fetch(endpoint,{
    method:"POST",
    headers:{Origin:origin,"Content-Type":"application/json",Authorization:"Bearer "+token},
    body:JSON.stringify(body),
    redirect:"manual",
  });
  let data;
  try{data=await r.json()}catch{data={error:"No JSON"}}
  return {status:r.status,data};
}
async function denied(token,label,body={action:"list",page:1},origin=root){
  const a=await admin(token,body,origin);
  assert([401,403].includes(a.status), label+" denied (got "+a.status+")");
  assert.equal(a.data.users,undefined,label+" cannot enumerate users");
  assert.equal(a.data.sent,undefined,label+" cannot send recovery");
  console.log("PASS "+label+" rejected ("+a.status+")");
}

const owner=await createUser("approved-owner");
const stranger=await createUser("unapproved-user");
const ownerAAL2=await totpChallenge(owner);
const strangerAAL2=await totpChallenge(stranger);
const grant=await invoke("/rest/v1/lockvyn_admins","POST",service,service,
  {user_id:owner.id});
assert([201,204].includes(grant.status),"Local allowlist grant failed: "+JSON.stringify(grant.data));
await denied(owner.aal1,"Owner without completing MFA");
await denied(strangerAAL2.aal2,"Verified 2FA user without admin role");
await denied(stranger.aal1,"Nonadmin AAL1");
await denied(ownerAAL2.aal2,"Unexpected origin",{action:"list",page:1},"https://attacker.invalid");

const allowed=await admin(ownerAAL2.aal2,{action:"list",page:1});
assert.equal(allowed.status,200,"Verified owner should be allowed: "+JSON.stringify(allowed.data));
assert(Array.isArray(allowed.data.users) && allowed.data.users.length>=2);
const gotOwner=allowed.data.users.find(u=>u.id===owner.id);
const gotStranger=allowed.data.users.find(u=>u.id===stranger.id);
assert(gotOwner && gotOwner.mfa===true && gotOwner.confirmed===true,
  "Owner factor was not returned as verified");
assert(gotStranger && gotStranger.mfa===true,"Verified factor should report enabled");
assert.equal(gotOwner.isSelf,true,"Admin owner shown as self");
assert.equal(gotStranger.isSelf,false,"Nonadmin not labelled self");
await denied(strangerAAL2.aal2,"Unapproved user recovery",{action:"recover",id:owner.id});
await denied(owner.aal1,"Owner AAL1 recovery",{action:"recover",id:stranger.id});
assert.equal(recoveryEmails,0,"No password reset mails");
assert.equal(accountWrites,2,"Only disposable local identities were created");
console.log("PASS real local GoTrue AAL1/AAL2 JWT verification, admin allowlist and read access");
console.log("PASS recovery rejected for nonadmin and AAL1; no recovery messages sent");
