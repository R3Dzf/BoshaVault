"use strict";
const assert=require("node:assert/strict");
const fs=require("node:fs");
const vm=require("node:vm");
const manifest=JSON.parse(fs.readFileSync("browser-extension/manifest.json","utf8"));
assert.equal(manifest.manifest_version,3);
assert.ok(manifest.permissions.includes("scripting"));
assert.ok(!manifest.permissions.includes("webAuthenticationProxy"),
  "Must not proxy or capture real authenticator assertions.");
const source=fs.readFileSync("browser-extension/passkey-conditional.js","utf8");
const restore=fs.readFileSync("browser-extension/passkey-restore.js","utf8");
let nativeCalls=0;
const original=function(opts){nativeCalls++;return Promise.resolve(opts?.publicKey?"explicit-passkey":"regular");};
const creds={get:original,create:()=>Promise.resolve("created")};
const env={navigator:{credentials:creds},Symbol,Promise,DOMException,Reflect,Object};
vm.runInNewContext(source,env);
assert.notEqual(creds.get,original);
(async()=>{
  await assert.rejects(creds.get({mediation:"conditional",publicKey:{challenge:[1]}}),
    err=>err.name==="NotAllowedError");
  assert.equal(nativeCalls,0,"Conditional passkey request must not call native WebAuthn");
  assert.equal(await creds.get({mediation:"required",publicKey:{challenge:[1]}}),"explicit-passkey");
  assert.equal(await creds.get({publicKey:{challenge:[1]}}),"explicit-passkey");
  assert.equal(await creds.get({mediation:"conditional",password:true}),"regular");
  assert.equal(await creds.create(),"created");
  vm.runInNewContext(source,env);
  await assert.rejects(creds.get({mediation:"conditional",publicKey:{challenge:[1]}}),
    err=>err.name==="NotAllowedError");
  vm.runInNewContext(restore,env);
  assert.equal(creds.get,original,"Disabling restores original browser CredentialContainer.get");
  assert.equal(await creds.get({mediation:"conditional",publicKey:{challenge:[1]}}),"explicit-passkey");
  vm.runInNewContext(restore,env);
  console.log("PASS conditional passkey UI blocked only on opt-in; explicit login and creation preserved; reversible");
})().catch(err=>{console.error(err);process.exitCode=1;});
