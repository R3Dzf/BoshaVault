import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import {
  redactSupportText,safeDiagnostics,parseUserTicketRequest,parseAdminTicketRequest,
  isAdminTicketAction,validTicketId
} from "../supabase/functions/lockvyn-admin/tickets.mjs";

const ticketId="f1000000-0000-4000-8000-000000000001";
const adminId="f2000000-0000-4000-8000-000000000002";
const raw="Bearer abcdefghijklmnopqrstuvwxyz password=hunter2 email user@example.invalid id f3000000-0000-4000-8000-000000000003";
const safe=redactSupportText(raw);
for(const secret of ["abcdefghijklmnopqrstuvwxyz","hunter2","user@example.invalid","f3000000-0000-4000-8000-000000000003"])
  assert.equal(safe.includes(secret),false,"secret/identifier should be removed");
assert.match(safe,/redacted/);

assert.equal(validTicketId(ticketId),true);
assert.equal(validTicketId("00000000-0000-0000-0000-000000000000"),false);
const diagnostic={platform:"windows",appVersion:"3.1.0",osVersion:"Windows 11",errorCode:"HTTP_503"};
assert.equal(safeDiagnostics(diagnostic,false).ok,false,"diagnostics cannot upload without consent");
assert.equal(safeDiagnostics(diagnostic,true).ok,true);
assert.equal(safeDiagnostics({...diagnostic,deviceId:"private"},true).ok,false,"diagnostic schema excludes identifiers");
const redactedDiagnostic=safeDiagnostics({...diagnostic,osVersion:"password=hunter2"},true);
assert.equal(redactedDiagnostic.ok,true);
assert.equal(JSON.stringify(redactedDiagnostic.diagnostics).includes("hunter2"),false,"diagnostic values are redacted before storage");

const create=parseUserTicketRequest({action:"ticketCreate",ticket:{
  subject:"Sync issue",body:raw,diagnosticConsent:true,diagnostics:diagnostic
}});
assert.equal(create.ok,true);
assert.equal(create.payload.body.includes("hunter2"),false);
assert.equal(create.payload.body.includes("user@example.invalid"),false);
assert.equal(create.payload.diagnosticConsent,true);
assert.equal(parseUserTicketRequest({action:"ticketCreate",ticket:{
  subject:"Sync issue",body:"Help",diagnosticConsent:false,diagnostics:diagnostic
}}).ok,false);
assert.equal(parseUserTicketRequest({action:"ticketCreate",ticket:{
  subject:"",body:"Help",diagnosticConsent:false,diagnostics:null
}}).ok,false);
assert.equal(parseUserTicketRequest({action:"ticketCreate",ticket:{
  subject:"Sync issue",body:"Help",diagnosticConsent:false,diagnostics:null,
  attachments:[{filename:"vault.json",content:"synthetic"}]
}}).ok,false,"arbitrary files and vault exports are not accepted as attachments");
assert.equal(parseUserTicketRequest({action:"ticketGet",ticketId}).action,"get");
assert.equal(parseUserTicketRequest({action:"ticketReply",ticketId,body:"Thanks"}).action,"reply");
assert.equal(parseUserTicketRequest({action:"ticketGet",ticketId:"bad"}).ok,false);
assert.equal(parseUserTicketRequest({action:"ticketDelete",ticketId}).ok,false);

assert.equal(parseAdminTicketRequest({action:"ticketAdminList"}).write,false);
assert.equal(parseAdminTicketRequest({action:"ticketAdminAssign",ticketId,assigneeId:adminId}).write,true);
assert.equal(parseAdminTicketRequest({action:"ticketAdminAssign",ticketId,assigneeId:"not-an-admin"}).ok,false);
assert.equal(parseAdminTicketRequest({action:"ticketAdminStatus",ticketId,status:"escalated",priority:"high"}).ok,true);
assert.equal(parseAdminTicketRequest({action:"ticketAdminStatus",ticketId,status:"wiped",priority:"high"}).ok,false);
assert.equal(parseAdminTicketRequest({action:"ticketAdminReply",ticketId,body:"We are looking into it."}).action,"reply");
assert.equal(isAdminTicketAction("ticketAdminAssign"),true);
assert.equal(isAdminTicketAction("ticketCreate"),false);
const android=readFileSync(new URL("../android/app/src/main/java/com/bosha/vault/MainActivity.java",import.meta.url),"utf8");
assert.match(android,/selectedId\[0\]=ticketId/,"Android ticket selection is retained for reply and status actions");
assert.match(android,/supportTicketList\(client,profile,store,list,thread,status,selectedId/,"Android refreshes the selected private thread");
assert.match(android,/check\(b,"Include optional sanitized diagnostics[\s\S]*?false\)/,"Android diagnostics consent is unchecked by default");
const windows=readFileSync(new URL("../src/BoshaVault.Windows/SupportTicketDialog.cs",import.meta.url),"utf8");
assert.match(windows,/IsChecked = false/,"Windows diagnostics consent is unchecked by default");
assert.match(windows,/d.Loaded \+= async \(_, _\) => await Run\(\(\) => LoadTickets\(\)\)/,"Windows loads the requester ticket list when opened");
assert.match(windows,/diagnosticConsent = consent.IsChecked == true/,"Windows sends the explicit consent decision");
console.log("PASS native clients: selected-ticket actions, initial load, explicit opt-in diagnostics");
console.log("PASS support tickets: redaction, explicit diagnostics consent, authorization maps, bounded actions");
