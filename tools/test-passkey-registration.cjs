"use strict";
const assert=require("node:assert/strict");
const fs=require("node:fs");
const vm=require("node:vm");
const source=fs.readFileSync("browser-extension/background.js","utf8");
let installed=[],apply=[],registeredListeners={},enabled=false;
const chrome={
  runtime:{
    id:"test-extension",
    onMessage:{addListener(){}},
    onInstalled:{addListener(fn){registeredListeners.install=fn;}},
    onStartup:{addListener(fn){registeredListeners.start=fn;}}
  },
  storage:{
    local:{async get(){return {blockConditionalPasskeys:enabled};}},
    onChanged:{addListener(fn){registeredListeners.change=fn;}}
  },
  scripting:{
    async getRegisteredContentScripts(){return installed;},
    async registerContentScripts(scripts){installed=scripts;},
    async unregisterContentScripts(){installed=[];},
    async executeScript(options){apply.push(options);}
  },
  tabs:{async query(){return [{id:73,url:"https://example.com/sign-in"}];}}
};
vm.runInNewContext(source,{chrome,URL,Promise,console});
const flush=async()=>{await new Promise(r=>setTimeout(r,25));};
(async()=>{
  assert.equal(typeof registeredListeners.install,"function");
  registeredListeners.install();await flush();
  assert.equal(installed.length,0,"Off by default: no injected page-world script");
  enabled=true;
  registeredListeners.change({blockConditionalPasskeys:{newValue:true}},"local");
  await flush();
  assert.equal(installed.length,1);
  assert.equal(installed[0].world,"MAIN");
  assert.equal(installed[0].runAt,"document_start");
  assert.equal(installed[0].allFrames,false);
  assert.equal(installed[0].js[0],"passkey-conditional.js");
  assert.equal(apply.at(-1).files[0],"passkey-conditional.js");
  enabled=false;
  registeredListeners.change({blockConditionalPasskeys:{newValue:false}},"local");
  await flush();
  assert.equal(installed.length,0);
  assert.equal(apply.at(-1).files[0],"passkey-restore.js");
  assert.equal(apply.at(-1).target.tabId,73);
  console.log("PASS opt-in MV3 main-world registration, active-tab application and safe unregister/restore");
})().catch(e=>{console.error(e);process.exitCode=1;});
