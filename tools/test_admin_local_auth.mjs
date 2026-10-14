#!/usr/bin/env node
// Real Supabase Auth + deployed-function contract, against a disposable LOCAL stack.
// No production projects, real customer records, email sends or paid infrastructure.
import { readFileSync } from "node:fs";
import { createHmac, createHash, randomUUID } from "node:crypto";
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
const ticketsEndpoint = endpoint + "/tickets";
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
  return {...user,aal2,totpSecret:secret,factorId:factor};
}
async function verifyExistingTotp(user,token){
  const challenged=await invoke("/auth/v1/factors/"+encodeURIComponent(user.factorId)+"/challenge",
    "POST",anon,token,{});
  assert.equal(challenged.status,200,"Challenge existing synthetic factor");
  const verified=await invoke("/auth/v1/factors/"+encodeURIComponent(user.factorId)+"/verify",
    "POST",anon,token,{challenge_id:challenged.data.id,code:totp(user.totpSecret)});
  assert.equal(verified.status,200,"Fresh TOTP verification: "+JSON.stringify(verified.data));
  const claims=JSON.parse(Buffer.from(verified.data.access_token.split(".")[1],"base64url").toString("utf8"));
  assert.equal(claims.aal,"aal2","Fresh factor must issue AAL2");
  return verified.data.access_token;
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
async function ticketRequest(token,body,origin=root,method="POST") {
  const response=await fetch(ticketsEndpoint,{
    method,headers:{Origin:origin,"Content-Type":"application/json",Authorization:"Bearer "+token},
    body:method==="POST"?JSON.stringify(body):undefined,redirect:"manual",
  });
  let data;try{data=await response.json()}catch{data={error:"No JSON"}}
  return {status:response.status,data,headers:response.headers};
}
async function dataApi(path,method,token,body,apiKey=anon){
  const response=await fetch(base+"/rest/v1/"+path,{
    method,
    headers:{
      apikey:apiKey,Authorization:"Bearer "+token,
      ...(body?{"Content-Type":"application/json"}:{}),
      Prefer:"return=representation",
    },
    body:body?JSON.stringify(body):undefined,
    redirect:"manual",
  });
  const raw=await response.text();
  let data=null;
  if(raw){try{data=JSON.parse(raw)}catch{data={error:raw.slice(0,500)}}}
  return {status:response.status,data};
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
// A third synthetic account without verified email proves server-side filters.
const pendingEmail="pending-"+suite+"@example.invalid";
const pending=await invoke("/auth/v1/admin/users","POST",service,service,
  {email:pendingEmail,password,email_confirm:false});
assert.equal(pending.status,200,"Create pending local account");
accountWrites++;
const ownerAAL2=await totpChallenge(owner);
const strangerAAL2=await totpChallenge(stranger);
const grant=await invoke("/rest/v1/lockvyn_admins","POST",service,service,
  {user_id:owner.id});
assert([201,204].includes(grant.status),"Local allowlist grant failed: "+JSON.stringify(grant.data));
const protectedGrant=await invoke("/rest/v1/lockvyn_admins","POST",service,service,
  {user_id:pending.data.id});
assert([201,204].includes(protectedGrant.status),"Synthetic protected admin grant failed");
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
assert.equal(allowed.data.total,3,"Global account totals include all users");
assert.equal(allowed.data.matching,3,"Unfiltered directory count");
assert.equal(allowed.data.stats.verified,2,"Global verified email count");
assert.equal(allowed.data.stats.mfaEnabled,2,"Global enrolled MFA count");
const search=await admin(ownerAAL2.aal2,{
  action:"list",page:1,search:"UNAPPROVED-USER"});
assert.equal(search.status,200,"Case-insensitive account search");
assert.equal(search.data.matching,1);
assert.equal(search.data.users[0].id,stranger.id);
const pendingFilter=await admin(ownerAAL2.aal2,{
  action:"list",page:1,emailState:"pending"});
assert.equal(pendingFilter.status,200);
assert.equal(pendingFilter.data.matching,1);
assert.equal(pendingFilter.data.users[0].email,pendingEmail);
const mfaOnly=await admin(ownerAAL2.aal2,{
  action:"list",page:1,mfaState:"enabled"});
assert.equal(mfaOnly.status,200);
assert.equal(mfaOnly.data.matching,2);
const paged=await admin(ownerAAL2.aal2,{
  action:"list",page:2});
assert.equal(paged.status,200);
assert.equal(paged.data.matching,3);
assert.equal(paged.data.users.length,0,"Offset must be applied on the server");
const invalid=await admin(ownerAAL2.aal2,{
  action:"list",page:1,mfaState:"bogus"});
assert.equal(invalid.status,400,"Reject unauthorized filter values");
assert.equal(gotOwner.isSelf,true,"Admin owner shown as self");
assert.equal(gotStranger.isSelf,false,"Nonadmin not labelled self");

// Phase 08: real local GoTrue AAL1 support flow and AAL2 agent workflow.
const supportUser=await createUser("support-requester");
const supportClaims=JSON.parse(Buffer.from(supportUser.aal1.split(".")[1],"base64url").toString("utf8"));
const activeTicketSession=await dataApi("rpc/lockvyn_auth_session_is_active","POST",service,{
  p_user_id:supportUser.id,p_session_id:supportClaims.session_id,
},service);
assert.equal(activeTicketSession.status,200,"Service role can verify the requester Auth session: "+JSON.stringify(activeTicketSession.data));
assert.equal(activeTicketSession.data,true,"New AAL1 Auth session is active");
const serviceTicketList=await dataApi("rpc/lockvyn_support_ticket_user_manage","POST",service,{
  p_action:"list",p_actor:supportUser.id,p_actor_session:supportClaims.session_id,p_ticket_id:null,p_payload:{},
},service);
assert.equal(serviceTicketList.status,200,"Service role can call the user ticket RPC: "+JSON.stringify(serviceTicketList.data));
const ticketPreflight=await fetch(ticketsEndpoint,{method:"OPTIONS",headers:{
  Origin:root,"Access-Control-Request-Method":"POST",
  "Access-Control-Request-Headers":"authorization,content-type",
}});
assert([200,204].includes(ticketPreflight.status),"Dashboard ticket API preflight is allowed");
assert(["*",root].includes(ticketPreflight.headers.get("access-control-allow-origin")),
  "Local Supabase may normalize preflight origin; browser API still requires allowlisted POST origin.");
assert.match(ticketPreflight.headers.get("access-control-allow-methods")||"",/POST/);
assert.match(ticketPreflight.headers.get("access-control-allow-headers")||"",/authorization.*content-type/i);
const foreignOriginTicket=await ticketRequest(supportUser.aal1,{action:"ticketList"},"https://attacker.invalid");
assert.equal(foreignOriginTicket.status,403,"Actual ticket API requests reject untrusted browser origins");
const createdSupportTicket=await ticketRequest(supportUser.aal1,{action:"ticketCreate",ticket:{
  subject:"Synthetic local sync issue",body:"Local test report with no account or vault data.",
  diagnosticConsent:true,diagnostics:{platform:"windows",appVersion:"3.1.0",osVersion:"Windows 11",errorCode:"SYNTHETIC_503"},
}});
assert.equal(createdSupportTicket.status,200,"Confirmed AAL1 user can create a ticket: "+JSON.stringify(createdSupportTicket.data));
const localTicketId=createdSupportTicket.data.ticket.id;
assert.match(localTicketId,/^[a-f0-9-]{36}$/i);
const otherOwnerRead=await ticketRequest(strangerAAL2.aal2,{action:"ticketGet",ticketId:localTicketId});
assert.equal(otherOwnerRead.status,404,"Ticket content is private to its requester");
const ownSupportTicket=await ticketRequest(supportUser.aal1,{action:"ticketGet",ticketId:localTicketId});
assert.equal(ownSupportTicket.status,200,"Requester can read own ticket at AAL1");
assert.equal(ownSupportTicket.data.diagnostics.payload.errorCode,"SYNTHETIC_503");
assert.equal(ownSupportTicket.data.ticket.requester_id,undefined,"Ticket response omits requester identifiers");
const unsupportedFile=await ticketRequest(supportUser.aal1,{action:"ticketCreate",ticket:{
  subject:"Synthetic file attempt",body:"This fixture must not store arbitrary files.",
  diagnosticConsent:false,diagnostics:null,attachments:[{filename:"vault.json",content:"synthetic"}],
}});
assert.equal(unsupportedFile.status,400,"Arbitrary file attachments are explicitly rejected");
const noConsent=await ticketRequest(supportUser.aal1,{action:"ticketCreate",ticket:{
  subject:"Synthetic consent check",body:"No diagnostics should be accepted without consent.",
  diagnosticConsent:false,diagnostics:{platform:"windows"},
}});
assert.equal(noConsent.status,400,"Diagnostics require explicit consent");
const deniedAal1Agent=await ticketRequest(supportUser.aal1,{action:"ticketAdminList"});
assert.equal(deniedAal1Agent.status,403,"AAL1 administrator cannot read the agent queue");
const deniedNonadminAgent=await ticketRequest(strangerAAL2.aal2,{action:"ticketAdminList"});
assert.equal(deniedNonadminAgent.status,403,"AAL2 nonadministrator cannot read the agent queue");
const localAgentQueue=await ticketRequest(ownerAAL2.aal2,{action:"ticketAdminList"});
assert.equal(localAgentQueue.status,200,"Allowlisted AAL2 administrator can read the queue");
assert(localAgentQueue.data.tickets.some(item=>item.id===localTicketId));
const localAgentTicket=await ticketRequest(ownerAAL2.aal2,{action:"ticketAdminGet",ticketId:localTicketId});
assert.equal(localAgentTicket.status,200);
assert.equal(localAgentTicket.data.diagnostics.payload.platform,"windows");
const badAgentConfirm=await ticketRequest(await verifyExistingTotp(ownerAAL2,owner.aal1),{
  action:"ticketAdminAssign",ticketId:localTicketId,assigneeId:owner.id,confirmId:"wrong",
});
assert.equal(badAgentConfirm.status,409,"Agent writes require typed ticket ID");
const assignedLocalTicket=await ticketRequest(await verifyExistingTotp(ownerAAL2,owner.aal1),{
  action:"ticketAdminAssign",ticketId:localTicketId,assigneeId:owner.id,confirmId:localTicketId,
});
assert.equal(assignedLocalTicket.status,200,"Agent assignment succeeds after fresh TOTP");
const escalatedLocalTicket=await ticketRequest(await verifyExistingTotp(ownerAAL2,owner.aal1),{
  action:"ticketAdminEscalate",ticketId:localTicketId,confirmId:localTicketId,
});
assert.equal(escalatedLocalTicket.status,200,"Agent can escalate the request");
assert.equal(escalatedLocalTicket.data.ticket.status,"escalated");
const statusLocalTicket=await ticketRequest(await verifyExistingTotp(ownerAAL2,owner.aal1),{
  action:"ticketAdminStatus",ticketId:localTicketId,status:"in_progress",priority:"high",confirmId:localTicketId,
});
assert.equal(statusLocalTicket.status,200,"Agent can update status and priority");
const agentReply=await ticketRequest(await verifyExistingTotp(ownerAAL2,owner.aal1),{
  action:"ticketAdminReply",ticketId:localTicketId,body:"Synthetic local agent response.",confirmId:localTicketId,
});
assert.equal(agentReply.status,200,"Agent can reply after fresh TOTP");
const visibleReply=await ticketRequest(supportUser.aal1,{action:"ticketGet",ticketId:localTicketId});
assert.equal(visibleReply.status,200);
assert(visibleReply.data.messages.some(message=>message.senderRole==="agent"&&message.body==="Synthetic local agent response."));
console.log("PASS real local Phase 08: AAL1 ownership, consented diagnostics, file rejection, allowlisted AAL2 assignment/escalation/status/reply and typed ID");

// Phase 03: opted-in device metadata and per-Auth-session cloud revocation.
// Two separate local GoTrue sessions prove that revoking one does not block the other.
const registrationPayload={
  p_consent:true,p_platform:"windows",p_os_version:"11",p_app_version:"3.1.0",
  p_display_name:"Synthetic laptop",p_schema_version:1,
};
const refusedConsent=await dataApi("rpc/lockvyn_register_cloud_device","POST",strangerAAL2.aal2,
  {...registrationPayload,p_consent:false});
assert([400,403].includes(refusedConsent.status),"Registry must require explicit opt-in");
const registeredOne=await dataApi("rpc/lockvyn_register_cloud_device","POST",strangerAAL2.aal2,
  registrationPayload);
assert.equal(registeredOne.status,200,"Register first opted-in session: "+JSON.stringify(registeredOne.data));
assert.match(registeredOne.data,/^[a-f0-9-]{36}$/i,"Registry returns an opaque record id");
const firstClaims=JSON.parse(Buffer.from(strangerAAL2.aal2.split(".")[1],"base64url").toString("utf8"));
assert.match(firstClaims.session_id,/^[a-f0-9-]{36}$/i);
const syncHeartbeat=await dataApi("rpc/lockvyn_record_cloud_device_sync","POST",strangerAAL2.aal2,{});
assert.equal(syncHeartbeat.status,200,"Successful cloud sync records a server timestamp");
const initialSyncStatus=await dataApi("rpc/lockvyn_cloud_sync_status","POST",strangerAAL2.aal2,{});
assert.equal(initialSyncStatus.status,200,"Owner can preflight cloud sync without reading ciphertext");
assert.equal(initialSyncStatus.data.suspended,false);
assert.equal(initialSyncStatus.data.snapshotCount,0);
assert.equal(initialSyncStatus.data.ciphertextBytes,0);
const secondSignIn=await invoke("/auth/v1/token?grant_type=password","POST",anon,null,
  {email:stranger.email,password});
assert.equal(secondSignIn.status,200,"Create a second synthetic Auth session");
const secondAAL2=await verifyExistingTotp(strangerAAL2,secondSignIn.data.access_token);
const registeredTwo=await dataApi("rpc/lockvyn_register_cloud_device","POST",secondAAL2,
  {...registrationPayload,p_display_name:"Synthetic second laptop"});
assert.equal(registeredTwo.status,200,"Register second independent Auth session");
assert.notEqual(registeredOne.data,registeredTwo.data,"Separate Auth sessions have separate registry IDs");
const deviceVaultSlot=randomUUID(),deviceSlot=randomUUID();
const syntheticCipher=Buffer.alloc(102,0x5a).toString("base64");
const deviceSnapshotPath="opaque_device_snapshots?select=owner_id,vault_slot,ciphertext_bytes&owner_id=eq."+
  stranger.id+"&vault_slot=eq."+deviceVaultSlot;
const deviceSnapshotBody={owner_id:stranger.id,vault_slot:deviceVaultSlot,device_slot:deviceSlot,
  ciphertext_base64:syntheticCipher,
  ciphertext_sha256:createHash("sha256").update(syntheticCipher).digest("hex")};
const deviceSnapshot=await dataApi(deviceSnapshotPath,"POST",strangerAAL2.aal2,deviceSnapshotBody);
assert.equal(deviceSnapshot.status,201,"Synthetic encrypted snapshot is accessible before revocation");
const derivedSize=await dataApi(deviceSnapshotPath,"GET",strangerAAL2.aal2,null);
assert.equal(derivedSize.status,200);
assert.equal(derivedSize.data[0].ciphertext_bytes,102,
  "Server derives raw ciphertext size even though this old-client payload omits the column");
const usedSyncStatus=await dataApi("rpc/lockvyn_cloud_sync_status","POST",strangerAAL2.aal2,{});
assert.equal(usedSyncStatus.status,200);
assert.equal(usedSyncStatus.data.snapshotCount,1);
assert.equal(usedSyncStatus.data.ciphertextBytes,102);
assert.equal(usedSyncStatus.data.quotaBytes,67108864);
const concurrentVault=randomUUID();
const concurrentRows=await Promise.all([randomUUID(),randomUUID()].map(async device=>dataApi(
  "opaque_device_snapshots","POST",strangerAAL2.aal2,{
    owner_id:stranger.id,vault_slot:concurrentVault,device_slot:device,
    ciphertext_base64:syntheticCipher,
    ciphertext_sha256:createHash("sha256").update(syntheticCipher).digest("hex"),
  })));
assert(concurrentRows.every(row=>row.status===201),
  "Two concurrent synthetic snapshot uploads both commit under the owner quota");
const concurrentRead=await dataApi("opaque_device_snapshots?select=device_slot&owner_id=eq."+
  stranger.id+"&vault_slot=eq."+concurrentVault,"GET",strangerAAL2.aal2,null);
assert.equal(concurrentRead.status,200);
assert.equal(concurrentRead.data.length,2,"Concurrent uploads leave both device rows visible");
const inventory=await admin(ownerAAL2.aal2,{action:"deviceList",id:stranger.id});
assert.equal(inventory.status,200,"Allowlisted admin can list opted-in sessions: "+JSON.stringify(inventory.data));
assert.equal(inventory.data.devices.length,2,"Only explicitly registered sessions appear");
assert(inventory.data.devices.some(d=>d.device_id===registeredOne.data&&d.last_sync_at),
  "Inventory includes session metadata and server-recorded last sync");
assert(!JSON.stringify(inventory.data.devices).includes(firstClaims.session_id),
  "Inventory never exposes Auth session identifiers");
assert.equal(typeof inventory.data.snapshotCount,"number","Admin receives safe quota usage");
assert.equal(inventory.data.ciphertextBytes,306,"Admin usage sums only ciphertext byte counts");
const filteredInventory=await admin(ownerAAL2.aal2,{action:"deviceList",id:stranger.id,
  platform:"windows",outcome:"success",sinceDays:30,deviceQuery:"Synthetic laptop"});
assert.equal(filteredInventory.status,200,"Admin can filter sync outcomes and software metadata");
assert.equal(filteredInventory.data.devices.length,1);
assert.equal(filteredInventory.data.devices[0].device_id,registeredOne.data);
const selfInventory=await admin(ownerAAL2.aal2,{action:"deviceList",id:owner.id});
assert.equal(selfInventory.status,403,"Administrator's own account is protected");
const protectedInventory=await admin(ownerAAL2.aal2,{action:"deviceList",id:pending.data.id});
assert.equal(protectedInventory.status,403,"Another administrator's inventory is denied");
await denied(owner.aal1,"AAL1 device inventory",{action:"deviceList",id:stranger.id});
const wrongDeviceEmail=await admin(ownerAAL2.aal2,{action:"deviceRevoke",id:stranger.id,
  deviceId:registeredOne.data,confirmEmail:"wrong@example.invalid"});
assert.equal(wrongDeviceEmail.status,409,"Typed target email is checked before revocation");
const freshOwnerToken=await verifyExistingTotp(ownerAAL2,owner.aal1);
const revokeOne=await admin(freshOwnerToken,{action:"deviceRevoke",id:stranger.id,
  deviceId:registeredOne.data,confirmEmail:stranger.email});
assert.equal(revokeOne.status,200,"Fresh AAL2 + TOTP admin revocation succeeds: "+JSON.stringify(revokeOne.data));
assert.equal(revokeOne.data.revoked,true);
const deviceAfter=await admin(freshOwnerToken,{action:"deviceList",id:stranger.id});
assert.equal(deviceAfter.status,200);
assert(deviceAfter.data.devices.find(d=>d.device_id===registeredOne.data)?.revoked_at,
  "Revoked session remains visible in inventory");
assert.equal(deviceAfter.data.devices.find(d=>d.device_id===registeredTwo.data)?.revoked_at,null,
  "Revoking one session leaves the other active");
const blockedOldSession=await dataApi(deviceSnapshotPath,"GET",strangerAAL2.aal2,null);
assert.equal(blockedOldSession.status,200);
assert.equal(blockedOldSession.data.length,0,"Revoked signed session can no longer read snapshots");
const activeSecondSession=await dataApi(deviceSnapshotPath,"GET",secondAAL2,null);
assert.equal(activeSecondSession.status,200);
assert.equal(activeSecondSession.data.length,1,"Second session still reads the account's encrypted snapshot");
const withdrewConsent=await dataApi("rpc/lockvyn_unregister_cloud_device","POST",secondAAL2,{});
assert.equal(withdrewConsent.status,200,"Second session can withdraw its inventory consent");
assert.equal(withdrewConsent.data,true);
const afterWithdrawInventory=await admin(freshOwnerToken,{action:"deviceList",id:stranger.id});
assert.equal(afterWithdrawInventory.status,200);
assert.equal(afterWithdrawInventory.data.devices.length,1,"Withdrawing consent removes only that session's registry metadata");
const afterWithdrawSnapshot=await dataApi(deviceSnapshotPath,"GET",secondAAL2,null);
assert.equal(afterWithdrawSnapshot.status,200);
assert.equal(afterWithdrawSnapshot.data.length,1,"Withdrawing inventory consent leaves cloud sync access intact");
const retainedDeviceSnapshot=await dataApi(deviceSnapshotPath,"GET",service,null,service);
assert.equal(retainedDeviceSnapshot.status,200);
assert.equal(retainedDeviceSnapshot.data.length,1,"Revocation does not delete encrypted data");
// Phase 04: only bounded metadata from synthetic local identities is visible.
await denied(owner.aal1,"AAL1 security feed",{action:"securityEvents"});
await denied(secondAAL2,"Nonadmin security feed",{action:"securityEvents"});
const securityFeed=await admin(freshOwnerToken,{action:"securityEvents",limit:100});
assert.equal(securityFeed.status,200,"AAL2 allowlisted owner can load security activity: "+JSON.stringify(securityFeed.data));
assert(Array.isArray(securityFeed.data.events));
const securityTypes=new Set(securityFeed.data.events.map(event=>event.eventType));
assert(securityTypes.has("cloud_session_registered"),"Opt-in registration creates a security event");
assert(securityTypes.has("cloud_session_revoked"),"Session revocation creates a security event");
assert(securityTypes.has("admin_step_up_denied"),"Verified AAL1 denial creates a bounded security signal");
assert(securityTypes.has("admin_allowlist_denied"),"Verified nonadmin denial creates a bounded security signal");
assert(securityFeed.data.alerts.newCloudSessions24h>=2,"Both synthetic cloud sessions appear in 24-hour counts");
assert(securityFeed.data.alerts.adminStepUpDenials15m>=1);
assert(securityFeed.data.alerts.adminAllowlistDenials15m>=1);
const securityJson=JSON.stringify(securityFeed.data);
assert(!securityJson.includes("@example.invalid"),"Security feed must not expose account email");
assert(!securityJson.includes(firstClaims.session_id),"Security feed must not expose Auth session identifiers");
assert(!securityJson.includes("ip_address")&&!securityJson.includes("user_agent"),
  "Security feed must not expose network metadata");
const syncFreshAdminToken=await verifyExistingTotp(ownerAAL2,owner.aal1);
const wrongSyncEmail=await admin(syncFreshAdminToken,{action:"syncControl",id:stranger.id,
  suspend:true,reasonCode:"support_review",confirmEmail:"wrong@example.invalid"});
assert.equal(wrongSyncEmail.status,409,"Typed target email blocks sync suspension");
const paused=await admin(syncFreshAdminToken,{action:"syncControl",id:stranger.id,
  suspend:true,reasonCode:"support_review",confirmEmail:stranger.email});
assert.equal(paused.status,200,"Fresh AAL2 + TOTP admin can pause cloud sync");
assert.equal(paused.data.suspended,true);
assert.equal(paused.data.changed,true);
assert.match(paused.data.warning,/Offline vault data and encrypted cloud snapshots are preserved/);
const statusWhilePaused=await dataApi("rpc/lockvyn_cloud_sync_status","POST",secondAAL2,{});
assert.equal(statusWhilePaused.status,200);
assert.equal(statusWhilePaused.data.suspended,true);
assert(statusWhilePaused.data.snapshotCount>=1);
const hiddenWhilePaused=await dataApi(deviceSnapshotPath,"GET",secondAAL2,null);
assert.equal(hiddenWhilePaused.status,200);
assert.equal(hiddenWhilePaused.data.length,0,"Restrictive RLS blocks snapshot reads while paused");
const pausedWrite=await dataApi("opaque_device_snapshots","POST",secondAAL2,{
  owner_id:stranger.id,vault_slot:randomUUID(),device_slot:randomUUID(),
  ciphertext_base64:syntheticCipher,
  ciphertext_sha256:createHash("sha256").update(syntheticCipher).digest("hex"),
});
assert(![200,201,204].includes(pausedWrite.status),"Restrictive RLS rejects uploads while paused");
const retainedDuringPause=await dataApi(deviceSnapshotPath,"GET",service,null,service);
assert.equal(retainedDuringPause.status,200);
assert.equal(retainedDuringPause.data.length,1,"Pausing sync leaves encrypted snapshots intact");
const resumeToken=await verifyExistingTotp(ownerAAL2,owner.aal1);
const resumed=await admin(resumeToken,{action:"syncControl",id:stranger.id,
  suspend:false,reasonCode:"support_review",confirmEmail:stranger.email});
assert.equal(resumed.status,200,"Fresh AAL2 + TOTP admin can resume cloud sync");
assert.equal(resumed.data.suspended,false);
assert.equal(resumed.data.changed,true);
const visibleAfterResume=await dataApi(deviceSnapshotPath,"GET",secondAAL2,null);
assert.equal(visibleAfterResume.status,200);
assert.equal(visibleAfterResume.data.length,1,"Resume restores access without replacing ciphertext");
const syncEvents=await admin(resumeToken,{action:"securityEvents",limit:100});
assert.equal(syncEvents.status,200);
const syncEventTypes=new Set(syncEvents.data.events.map(event=>event.eventType));
assert(syncEventTypes.has("cloud_sync_suspended")&&syncEventTypes.has("cloud_sync_resumed"),
  "Pause and resume are visible in the privacy-limited Security Center");
assert(!JSON.stringify(syncEvents.data).includes("@example.invalid"));
console.log("PASS Phase 05 local GoTrue: concurrent uploads, derived usage, filtered admin activity, fresh-TOTP pause/resume, RLS, and ciphertext retention");
console.log("PASS Phase 04 local GoTrue: AAL2 feed, denial signals, opt-in/revocation events and metadata privacy");
console.log("PASS Phase 03 local GoTrue: opt-in, server heartbeat, admin inventory, fresh-TOTP session revocation, per-session RLS, and ciphertext retention");
await denied(strangerAAL2.aal2,"Unapproved user recovery",{action:"recover",id:owner.id});
await denied(owner.aal1,"Owner AAL1 recovery",{action:"recover",id:stranger.id});
// Phase 02: real free/local GoTrue and Postgres support action tests.
// The target is a disposable synthetic user; no production Auth mutation.
await denied(owner.aal1,"AAL1 support preparation",{
  action:"supportPrepare",id:stranger.id,kind:"suspend_24h",reason:"lost_device"});
await denied(strangerAAL2.aal2,"Non-admin support preparation",{
  action:"supportPrepare",id:pending.data.id,kind:"suspend_24h",reason:"lost_device"});
const selfSupport=await admin(ownerAAL2.aal2,{
  action:"supportPrepare",id:owner.id,kind:"suspend_24h",reason:"lost_device"});
assert.equal(selfSupport.status,403,"Protected owner cannot be suspended");
const createdCase=await admin(ownerAAL2.aal2,{
  action:"supportPrepare",id:stranger.id,kind:"suspend_24h",reason:"lost_device"});
assert.equal(createdCase.status,200,"Prepare real local account case: "+JSON.stringify(createdCase.data));
assert(typeof createdCase.data.caseId==="string");
const wrongEmail=await admin(ownerAAL2.aal2,{
  action:"supportExecute",id:stranger.id,kind:"suspend_24h",reason:"lost_device",
  caseId:createdCase.data.caseId,confirmEmail:"not-the-target@example.invalid"});
assert.equal(wrongEmail.status,409,"Wrong account email blocks privileged action");
const action=await admin(ownerAAL2.aal2,{
  action:"supportExecute",id:stranger.id,kind:"suspend_24h",reason:"lost_device",
  caseId:createdCase.data.caseId,confirmEmail:stranger.email});
assert.equal(action.status,200,"Real local suspension: "+JSON.stringify(action.data));
assert.equal(action.data.status,"completed");
const replay=await admin(ownerAAL2.aal2,{
  action:"supportExecute",id:stranger.id,kind:"suspend_24h",reason:"lost_device",
  caseId:createdCase.data.caseId,confirmEmail:stranger.email});
assert.equal(replay.status,409,"Already-consumed case cannot replay");
const afterSupport=await invoke("/auth/v1/admin/users/"+stranger.id,"GET",service,service,null);
assert.equal(afterSupport.status,200);
assert(Date.parse(afterSupport.data.banned_until)>Date.now(),
  "Synthetic cloud user is banned for a finite time");
const supportLog=await admin(ownerAAL2.aal2,{action:"supportHistory"});
assert.equal(supportLog.status,200);
assert(supportLog.data.cases.some(c=>c.id===createdCase.data.caseId &&
  c.state==="completed" && c.kind==="suspend_24h"),"Support action must be logged");
console.log("PASS real local support: AAL1/nonadmin/self denied, typed email, finite ban and no replay");
assert.equal(recoveryEmails,0,"No password reset mails");
assert.equal(accountWrites,4,"Only disposable local identities were created");
console.log("PASS real local GoTrue AAL1/AAL2 JWT verification, admin allowlist and read access");
console.log("PASS recovery rejected for nonadmin and AAL1; no recovery messages sent");


const dashboardPage=await fetch(endpoint,{method:"GET",redirect:"manual"});
assert.equal(dashboardPage.status,200,"Local admin dashboard HTML is served");
assert.match(dashboardPage.headers.get("content-security-policy")||"",/script-src 'nonce-/);
const dashboardHtml=await dashboardPage.text();
for(const required of [
  "Support tickets",
  "id=\"ticket-confirm-id\" autocomplete=\"off\" maxlength=\"36\"",
  "ticketAdminAssign",
  "Consented diagnostics",
  "Registered cloud sessions",
  "Encrypted cloud sync",
  "Cloud sync activity filters",
  "id=\"cloud-sync-usage\" role=\"status\" aria-live=\"polite\"",
  "id=\"pause-cloud-sync\" type=\"button\" hidden",
  "id=\"resume-cloud-sync\" type=\"button\" hidden",
  "id=\"cloud-sync-otp\" inputmode=\"numeric\" autocomplete=\"one-time-code\"",
  "syncControl",
  "SYNC_OUTCOME_LABELS",
  "SYNC_ERROR_LABELS",
  "cloud_sync_suspended",
  "Offline vault data and encrypted cloud snapshots are preserved.",
  "Only sessions the user explicitly opted in appear here",
  "self-reported; they do not prove a physical device",
  "Fresh code from your own authenticator app",
  "It does not erase offline vault data or invalidate the Auth refresh token",
  "deviceRevoke",
  "aria-labelledby=\"device-revoke-heading\"",
  "aria-live=\"polite\" tabindex=\"-1\"",
  "pendingDeviceTrigger",
  "deviceRevocationBusy",
  "supportTrigger",
  "id=\"directory-heading\" tabindex=\"-1\"",
  "id=\"cloud-device-heading\" tabindex=\"-1\"",
  "restoreFocus:true",
  "cloud-device-state\").focus()",
]){
  assert(dashboardHtml.includes(required),"Admin session UI must include: "+required);
}
const adminScriptStart=dashboardHtml.indexOf('<script nonce="');
const adminScriptBodyStart=dashboardHtml.indexOf(">",adminScriptStart)+1;
const adminScriptEnd=dashboardHtml.lastIndexOf("</script>");
assert(adminScriptStart>=0&&adminScriptBodyStart>adminScriptStart&&adminScriptEnd>=adminScriptBodyStart,
  "Admin script must be present");
new Function(dashboardHtml.slice(adminScriptBodyStart,adminScriptEnd));
console.log("PASS local admin session flow exposes named controls, focus return, busy guard, and syntactically valid UI script");
const securePage=await fetch(endpoint+"/security",{method:"GET",redirect:"manual"});
assert.equal(securePage.status,200,"Isolated user-owned security page is available");
assert.match(securePage.headers.get("content-security-policy")||"",/script-src 'nonce-/);
const secureHtml=await securePage.text();
assert(secureHtml.includes("logout?scope=global"));
assert(!secureHtml.includes("SUPABASE_SERVICE_ROLE_KEY"));
console.log("PASS self-service security page uses nonce CSP and no service-role credentials");

// Real provider global logout: only the user's JWT authorizes it, never an
// administrator action or an undocumented write to auth.sessions.
const alternate1=await invoke("/auth/v1/token?grant_type=password","POST",anon,null,
  {email:owner.email,password});
const alternate2=await invoke("/auth/v1/token?grant_type=password","POST",anon,null,
  {email:owner.email,password});
assert.equal(alternate1.status,200,"Synthetic login 1");
assert.equal(alternate2.status,200,"Synthetic login 2");
assert(alternate1.data.refresh_token && alternate2.data.refresh_token);
const oldAccessToken=ownerAAL2.aal2;
const oldClaims=JSON.parse(Buffer.from(oldAccessToken.split(".")[1],"base64url").toString("utf8"));
assert.match(oldClaims.session_id,/^[a-f0-9-]{36}$/i,"JWT needs a session_id for revocation checks");
const cipher=Buffer.alloc(102,0x5a).toString("base64");
assert.equal(cipher.length,136,"synthetic ciphertext fixture meets minimum opaque length");
const logoutVaultSlot=randomUUID(),logoutDeviceSlot=randomUUID();
const snapshotPath="opaque_device_snapshots?select=owner_id,vault_slot&owner_id=eq."+owner.id+"&vault_slot=eq."+logoutVaultSlot;
const snapshotBody={
  owner_id:owner.id,vault_slot:logoutVaultSlot,device_slot:logoutDeviceSlot,
  ciphertext_base64:cipher,ciphertext_sha256:createHash("sha256").update(cipher).digest("hex")
};
const savedSnapshot=await dataApi(snapshotPath,"POST",oldAccessToken,snapshotBody);
assert.equal(savedSnapshot.status,201,"live owner session can store opaque encrypted sync fixture: "+JSON.stringify(savedSnapshot.data));
const snapshotBefore=await dataApi(snapshotPath,"GET",oldAccessToken,null);
assert.equal(snapshotBefore.status,200);
assert.equal(snapshotBefore.data.length,1,"owner can read their own snapshot before logout");
const directSessionProbe=await dataApi("rpc/lockvyn_auth_session_is_active","POST",oldAccessToken,{
  p_user_id:owner.id,p_session_id:oldClaims.session_id
});
assert.notEqual(directSessionProbe.status,200,"authenticated users cannot call the service-only session checker");
const pairingId=randomUUID();
const pairingPath="device_pairing_sessions?select=session_id,state&session_id=eq."+pairingId;
const pairingBody={session_id:pairingId,owner_id:owner.id,initiator_public_key:"A".repeat(120)};
const savedPairing=await dataApi(pairingPath,"POST",oldAccessToken,pairingBody);
assert.equal(savedPairing.status,201,"live owner session can create ephemeral pairing row: "+JSON.stringify(savedPairing.data));
const pairingBefore=await dataApi(pairingPath,"GET",oldAccessToken,null);
assert.equal(pairingBefore.status,200);
assert.equal(pairingBefore.data.length,1,"owner can read their own pairing row before logout");
const revoked=await invoke("/auth/v1/logout?scope=global","POST",anon,oldAccessToken,null);
assert([200,204].includes(revoked.status),"Local GoTrue global logout must succeed");
for(const oldRefresh of [alternate1.data.refresh_token,alternate2.data.refresh_token]){
  const attempted=await invoke("/auth/v1/token?grant_type=refresh_token","POST",anon,null,
    {refresh_token:oldRefresh});
  assert([400,401,403].includes(attempted.status),
    "Previously active session refresh must fail after global logout");
}
const staleAdmin=await admin(oldAccessToken,{action:"list",page:1});
assert([401,403].includes(staleAdmin.status),"global logout blocks stale access JWTs from admin operations");
assert.equal(staleAdmin.data.users,undefined,"revoked admin JWT must not list users");
const snapshotAfter=await dataApi(snapshotPath,"GET",oldAccessToken,null);
assert.equal(snapshotAfter.status,200,"signed-but-revoked JWT remains parseable by PostgREST");
assert.equal(snapshotAfter.data.length,0,"revoked access JWT cannot read encrypted cloud snapshots");
const pairingAfter=await dataApi(pairingPath,"GET",oldAccessToken,null);
assert.equal(pairingAfter.status,200);
assert.equal(pairingAfter.data.length,0,"revoked access JWT cannot read device-pairing data");
const blockedInsert=await dataApi("opaque_device_snapshots?select=vault_slot","POST",oldAccessToken,{
  owner_id:owner.id,vault_slot:randomUUID(),device_slot:randomUUID(),
  ciphertext_base64:cipher,ciphertext_sha256:createHash("sha256").update(cipher).digest("hex")
});
assert.equal(blockedInsert.status,403,"revoked access JWT cannot insert cloud snapshots");
const retainedSnapshot=await dataApi(snapshotPath,"GET",service,null,service);
assert.equal(retainedSnapshot.status,200);
assert.equal(retainedSnapshot.data.length,1,"revocation blocks access without deleting encrypted data");
const retainedPairing=await dataApi(pairingPath,"GET",service,null,service);
assert.equal(retainedPairing.status,200);
assert.equal(retainedPairing.data.length,1,"revocation does not mutate pairing data");
await dataApi(snapshotPath,"DELETE",service,null,service);
await dataApi(pairingPath,"DELETE",service,null,service);
console.log("PASS global logout blocks stale access JWTs from admin, encrypted snapshot, and device-pairing operations while preserving data");
console.log("PASS real local GoTrue global logout revokes refresh across devices (JWT expiry caveat retained)");
