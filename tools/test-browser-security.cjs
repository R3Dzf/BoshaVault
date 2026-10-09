"use strict";
const assert=require("node:assert/strict");
const fs=require("node:fs");
const vm=require("node:vm");
const source=fs.readFileSync("browser-extension/background.js","utf8");
const host="https://github.com";
const entryId="c30c6cdb-6ac3-4e80-a87a-42f9d1a7a3c1";
const sender={
  id:"extension-test",frameId:0,documentId:"document-1",
  url:host+"/login",tab:{id:101,url:host+"/login"}
};

function setup({changed=false,tabOrigin=host}={}) {
  let handler, nativeCalls=0,frames=0;
  const chrome={
    runtime:{
      id:"extension-test",
      onMessage:{addListener(fn){handler=fn;}},
      async sendNativeMessage(name,message){
        nativeCalls++;
        assert.equal(name,"com.boshavault.desktop");
        return {status:"filled",username:"fakeuser",password:"fake-password-to-protect"};
      }
    },
    webNavigation:{async getFrame(){
      frames++;
      return {documentId:changed&&frames>1?"document-2":"document-1",
        url:tabOrigin+"/login"};
    }},
    tabs:{async get(){return {url:tabOrigin+"/login"};}}
  };
  vm.runInNewContext(source,{chrome,URL,Promise,console});
  return {
    request(message,from=sender){
      return new Promise(resolve=>{
        const keep=handler(message,from,resolve);
        assert.equal(keep,true);
      });
    },
    nativeCalls:()=>nativeCalls
  };
}
(async()=>{
  let scenario=setup();
  let reply=await scenario.request({op:"fill",entryId});
  assert.equal(reply.status,"filled","same document may receive user-approved fill");
  assert.equal(scenario.nativeCalls(),1);

  scenario=setup({changed:true});
  reply=await scenario.request({op:"fill",entryId});
  assert.equal(reply.status,"denied","navigated tab must never receive secret");
  assert.equal(reply.password,undefined);

  scenario=setup({tabOrigin:"https://malicious.example"});
  reply=await scenario.request({op:"fill",entryId});
  assert.equal(reply.status,"denied","different origin denied before native communication");
  assert.equal(scenario.nativeCalls(),0);

  scenario=setup();
  reply=await scenario.request({op:"fill",entryId:"bad"});
  assert.equal(reply.status,"denied","invalid account identifier rejected");
  assert.equal(scenario.nativeCalls(),0);

  scenario=setup();
  reply=await scenario.request({op:"update",entryId,
    username:"user@example.com",password:"CorrectStrongRandom1234!"});
  assert.equal(reply.status,"filled","synthetic transport response passes when document is live");
  assert.equal(scenario.nativeCalls(),1);
  console.log("PASS Chromium extension document identity, navigation race, origin, and invalid ID checks");
})().catch(e=>{console.error(e);process.exitCode=1;});
