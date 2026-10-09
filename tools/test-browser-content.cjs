"use strict";
const assert=require("node:assert/strict");
const fs=require("node:fs");
const vm=require("node:vm");

const handlers={};
let popup=null;
class FakeElement {
  constructor(tag="div"){
    this.tagName=tag.toUpperCase(); this.style={};this.children=[];
    this.events={};this.isConnected=true;this.value="";this.className="";
    this.disabled=false;this.readOnly=false;this.maxLength=-1;
    this.type=tag==="input"?"text":tag;
    this.name="";this.id="";this.autocomplete="";
  }
  setAttribute(key,value){this[key]=value;}
  getAttribute(key){return this[key]||null;}
  append(...items){this.children.push(...items);}
  remove(){this.isConnected=false; if(popup===this)popup=null;}
  contains(other){return this!==other && this.children.includes(other);}
  attachShadow(){const shadow=new FakeElement("shadow");this.shadow=shadow;return shadow;}
  addEventListener(type,callback){this.events[type]=callback;}
  getBoundingClientRect(){return {width:140,height:30,left:15,top:45,bottom:75};}
  dispatchEvent(){return true;}
  closest(){return null;}
  querySelectorAll(){return [];}
}
class HTMLInputElement extends FakeElement { constructor(type){super("input");this.type=type;} }
class HTMLFormElement extends FakeElement {
  constructor(){super("form");this.fields=[]; }
  querySelectorAll(selector){
    if(selector.includes("password"))return this.fields.filter(f=>f.type==="password");
    return this.fields.filter(f=>f.type!=="password");
  }
  getAttribute(){return null;}
}
const username=new HTMLInputElement("email");
username.name="email";username.autocomplete="username";
const password=new HTMLInputElement("password");
password.name="password";password.autocomplete="current-password";
const form=new HTMLFormElement();
form.fields=[username,password];
username.form=form;password.form=form;
let nativeFills=0;
const origin="https://github.com";
const location={protocol:"https:",origin,href:origin+"/login",hostname:"github.com"};
const document={
  activeElement:password,
  createElement:tag=>new FakeElement(tag),
  body:{append(el){popup=el;}},
  documentElement:{append(el){popup=el;}},
  querySelectorAll:()=>[username,password],
  addEventListener:(event,handler)=>handlers[event]=handler
};
const window={innerWidth:800,innerHeight:650,addEventListener:()=>{}};
window.top=window;
const chrome={runtime:{
  async sendMessage(req){
    if(req.op==="list")return {status:"ok",accounts:[{
      id:"fd6f16bf-f0cf-4b5a-b087-e3f998931a8a",title:"GitHub",username:"demo@example.com"
    }]};
    if(req.op==="fill"){nativeFills++;return {status:"filled",username:"demo@example.com",password:"SyntheticOnlyPassword123!"};}
    return {status:"unavailable"};
  },
  onMessage:{addListener(){}}
}};
const env={
  window,location,document,chrome,URL,HTMLInputElement,HTMLFormElement,
  getComputedStyle:()=>({visibility:"visible"}),
  innerWidth:800,innerHeight:650,Event:class{},
  setTimeout,clearTimeout
};
vm.runInNewContext(fs.readFileSync("browser-extension/content.js","utf8"),env);
(async()=>{
  handlers.focusin({target:password});
  await new Promise(resolve=>setTimeout(resolve,145));
  assert.ok(popup,"Login suggestion popup should be visible");
  const shadow=popup.shadow;
  assert.ok(shadow,"Suggestion UI must use isolated Shadow DOM");
  const panel=shadow.children.find(x=>x.className==="panel");
  assert.ok(panel,"Popup panel exists");
  const accountButton=panel.children.find(x=>x.tagName==="BUTTON" &&
    x.textContent?.includes("demo@example.com"));
  assert.ok(accountButton,"Matched account appears in dropdown");
  // Browser retargets pointerdown inside CLOSED ShadowRoot to the host.
  // A past bug hid the popup on pointerdown before the click action ran.
  handlers.pointerdown({target:popup});
  assert.ok(popup?.isConnected,"Clicking inside closed Shadow DOM must not hide the popup");
  accountButton.events.pointerdown({preventDefault(){}});
  accountButton.events.click({stopPropagation(){}});
  await new Promise(resolve=>setTimeout(resolve,20));
  assert.equal(nativeFills,1,"Account click must reach credential request");
  assert.equal(username.value,"demo@example.com");
  assert.equal(password.value,"SyntheticOnlyPassword123!");
  console.log("PASS browser suggestion renders, survives Shadow DOM pointerdown, and fills username/password");
})().catch(e=>{console.error(e);process.exitCode=1;});
