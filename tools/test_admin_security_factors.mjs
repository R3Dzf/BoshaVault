#!/usr/bin/env node
// Exercise the actual rendered self-service security page with synthetic Auth responses.
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const sourcePath = new URL("../supabase/functions/lockvyn-admin/security.ts", import.meta.url);
const source = readFileSync(sourcePath, "utf8");
const tsSignature = "export function securityHtml(n: string, publicKey: string, apiUrl: string): string {";
assert(source.includes(tsSignature), "security page entry signature changed; update this focused test");
const executable = source.replace(tsSignature,
  "globalThis.securityHtml = function securityHtml(n, publicKey, apiUrl) {")
  .replace("const safe = (v: string) =>", "const safe = (v) =>");
const renderer = {};
vm.runInNewContext(executable, renderer);
const html = renderer.securityHtml("test_nonce", "public-test-key", "https://project.supabase.co");
assert(html.includes('id="factor-choice"'));
assert(html.includes('nonce="test_nonce"'));
const inline = [...html.matchAll(/<script nonce="test_nonce">([\s\S]*?)<\/script>/g)];
assert.equal(inline.length, 1, "expected exactly one nonce-protected page script");

class FakeElement {
  constructor(id = "") {
    this.id = id; this.hidden = false; this.value = ""; this.textContent = "";
    this.className = ""; this.disabled = false; this.listeners = Object.create(null);
    this.children = []; this.options = this.children;
    this.submitButton = { disabled: false };
  }
  addEventListener(event, handler) { this.listeners[event] = handler; }
  querySelector(selector) { assert.equal(selector, "button"); return this.submitButton; }
  replaceChildren(...children) {
    this.children = children; this.options = this.children;
    this.value = this.children[0]?.value || "";
  }
  appendChild(child) {
    this.children.push(child); this.options = this.children;
    if (!this.value) this.value = child.value;
    return child;
  }
}
const ids = ["message","email","password","otp","factor-choice","sign-in",
  "choose-factor","start-factor","choose-another","verify-otp","confirm-box","revoke"];
const elements = Object.fromEntries(ids.map(id => [id, new FakeElement(id)]));
const document = {
  getElementById(id) { return elements[id] ||= new FakeElement(id); },
  createElement(tag) { assert.equal(tag, "option"); return new FakeElement(); }
};
const userId = "11111111-1111-4111-8111-111111111111";
const primaryId = "22222222-2222-4222-8222-222222222222";
const backupId = "33333333-3333-4333-8333-333333333333";
function token(claims) {
  const payload = btoa(JSON.stringify(claims)).replace(/=/g, "").replace(/\+/g, "-").replace(/\//g, "_");
  return "header." + payload + ".signature";
}
const aal1 = token({sub:userId,aal:"aal1"});
const aal2 = token({sub:userId,aal:"aal2"});
const calls = [], factorChallenges = [], verifiedCodes = [];
let confirmationCount = 0;
async function fetchMock(rawUrl, options) {
  const url = new URL(String(rawUrl));
  calls.push({url, options});
  const path = url.pathname + url.search;
  const body = options.body ? JSON.parse(options.body) : null;
  const ok = data => ({ok:true,status:200,text:async()=>data===null?"":JSON.stringify(data)});
  if(path==="/auth/v1/token?grant_type=password")
    return ok({access_token:aal1,user:{id:userId}});
  if(path==="/auth/v1/user") {
    const auth = options.headers.Authorization || "";
    assert(auth==="Bearer "+aal1||auth==="Bearer "+aal2,"only this owner's Auth token may be used");
    return ok({id:userId,factors:[
      {id:primaryId,factor_type:"totp",status:"verified",friendly_name:"Primary authenticator"},
      {id:backupId,factor_type:"totp",status:"verified",friendly_name:"<img src=x onerror=alert(1)>"},
      {id:"bad-id",factor_type:"totp",status:"verified",friendly_name:"Invalid factor"},
      {id:"44444444-4444-4444-8444-444444444444",factor_type:"totp",status:"unverified",friendly_name:"Pending"}
    ]});
  }
  const challenge=path.match(/^\/auth\/v1\/factors\/([^/]+)\/challenge$/);
  if(challenge){factorChallenges.push(decodeURIComponent(challenge[1]));return ok({id:"challenge-"+factorChallenges.length});}
  const verify=path.match(/^\/auth\/v1\/factors\/([^/]+)\/verify$/);
  if(verify){verifiedCodes.push({factor:decodeURIComponent(verify[1]),body});return ok({access_token:aal2});}
  if(path==="/auth/v1/logout?scope=global") return ok(null);
  throw new Error("Unexpected synthetic Auth endpoint: "+path);
}
vm.runInNewContext(inline[0][1], {
  document, fetch:fetchMock, confirm:()=>{confirmationCount++;return true;},
  URL, encodeURIComponent, atob, AbortSignal, Promise, JSON, Set, console
});
const submit=elements["sign-in"].listeners.submit;
assert.equal(typeof submit,"function","sign-in handler registered");
await submit({preventDefault(){}});
assert.equal(elements["password"].value,"","password is cleared after sign-in");
assert.equal(elements["choose-factor"].hidden,false,"factor selection shown");
assert.equal(elements["verify-otp"].hidden,true,"no challenge requested before selection");
assert.equal(elements["factor-choice"].options.length,2,"only verified TOTP factors are offered");
assert.equal(elements["factor-choice"].options[1].textContent,"<img src=x onerror=alert(1)>",
  "untrusted factor labels are inserted as text, not HTML");
assert.equal(elements["factor-choice"].options[1].children.length,0,"factor label cannot create markup");

assert.equal(elements["factor-choice"].value,primaryId,"the first verified factor is the safe default");
await elements["start-factor"].listeners.click();
assert.deepEqual(factorChallenges,[primaryId],"the default authenticator receives its own challenge");
assert.equal(elements["verify-otp"].hidden,false);
assert.equal(elements["choose-another"].hidden,false,"user can switch to another factor");
await elements["choose-another"].listeners.click();
assert.equal(elements["choose-factor"].hidden,false,"switching returns to the factor chooser");
assert.equal(elements["verify-otp"].hidden,true,"the prior factor challenge is discarded");
elements["factor-choice"].value=backupId;
await elements["start-factor"].listeners.click();
assert.deepEqual(factorChallenges,[primaryId,backupId],"the chosen backup factor receives a new challenge");
assert.equal(elements["verify-otp"].hidden,false);

elements["otp"].value="654321";
await elements["verify-otp"].listeners.submit({preventDefault(){}});
assert.equal(verifiedCodes.length,1);
assert.equal(verifiedCodes[0].factor,backupId);
assert.equal(verifiedCodes[0].body.challenge_id,"challenge-2");
assert.equal(verifiedCodes[0].body.code,"654321");
assert.equal(elements["confirm-box"].hidden,false,"fresh AAL2 must be reached before final consent");
assert.equal(elements["message"].textContent,"Authenticator verified. Final consent is required.");

await elements["revoke"].listeners.click();
assert.equal(confirmationCount,1,"global logout requires the browser's final confirmation");
assert(calls.some(call=>(call.url.pathname+call.url.search)==="/auth/v1/logout?scope=global"));
assert(calls.every(call=>call.url.origin==="https://project.supabase.co"),"Auth requests must go directly to Supabase");
assert(calls.every(call=>call.options.credentials==="omit"&&call.options.cache==="no-store"));
assert(!calls.some(call=>call.url.pathname.includes("lockvyn-admin")),"no privileged admin endpoint is involved");
console.log("PASS: backup TOTP selection, text-only factor labels, fresh AAL2, final consent and owner-only global logout");
