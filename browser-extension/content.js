"use strict";

// No background password-field harvesting: read username only after the user
// explicitly chooses Save, and only from this focused login form.
(() => {
  if (window.top !== window || location.protocol !== "https:") return;
  const pwSelector = 'input[type="password"],input[autocomplete="current-password"],input[autocomplete="new-password"]';
  let frame=null, shadow=null, panel=null, focus=null, sequence=0, draft=null, origin=location.origin;

  function visible(input) {
    if (!(input instanceof HTMLInputElement) || !input.isConnected ||
        input.disabled || input.readOnly) return false;
    const r=input.getBoundingClientRect();
    return r.width>12 && r.height>9 &&
      getComputedStyle(input).visibility!=="hidden";
  }
  // Decline forms visibly configured to send credentials to a different
  // origin. This cannot detect every JavaScript-controlled submit handler.
  function safeFormDestination(form) {
    if (!(form instanceof HTMLFormElement)) return true;
    let target;
    try { target = new URL(form.getAttribute("action") || location.href,location.href); }
    catch { return false; }
    if (target.origin !== location.origin || target.protocol !== "https:")return false;
    for (const button of form.querySelectorAll("[formaction]")) {
      if (button.disabled)continue;
      try {
        const url = new URL(button.getAttribute("formaction"),location.href);
        if (url.origin !== location.origin || url.protocol !== "https:")return false;
      }catch{return false;}
    }
    return true;
  }
  function credentialContext(input) {
    if (!visible(input)) return null;
    const form=input.form || input.closest("form") || document;
    if (!safeFormDestination(form)) return null;
    const pw=[...form.querySelectorAll(pwSelector)].filter(visible);
    if (!pw.length) return null;
    const pass=pw.find(e=>e.autocomplete==="new-password") || (pw.includes(input)?input:pw[0]);
    if(pass.type!=="password")return null;
    if (!pw.includes(input) && !["text","email",""].includes(input.type)) return null;
    const user=[...form.querySelectorAll('input:not([type="hidden"]):not([type="password"])')]
      .filter(visible).find(e=>{
        const hint=(e.autocomplete || "").toLowerCase();
        return hint==="username" || hint==="email" || e.type==="email" ||
          /user(name)?|login|email/i.test((e.name || "")+" "+(e.id || ""));
      }) || (pw.includes(input)?null:input);
    const confirm=pw.find(e=>e!==pass &&
      (e.autocomplete==="new-password" ||
       /confirm|repeat|verify|retype/i.test((e.name || "")+" "+(e.id || "")))) || null;
    return {pass,user,confirm,anchor:input,form};
  }
  function setup() {
    if (frame) return;
    frame=document.createElement("div");
    frame.setAttribute("data-boshavault-ui","1");
    frame.style.cssText="position:fixed;z-index:2147483646;width:310px;max-width:calc(100vw - 16px);pointer-events:auto;";
    shadow=frame.attachShadow({mode:"closed"});
    const style=document.createElement("style");
    style.textContent=".panel{box-sizing:border-box;width:100%;background:#fff;color:#242335;border:1px solid #dcd5f9;border-radius:12px;box-shadow:0 9px 26px #18122a3b;padding:10px;font:13px system-ui,sans-serif}.top{font-weight:700;padding:5px;color:#7762df}.sub{font-size:11px;color:#777;overflow-wrap:anywhere;padding:0 6px 7px}button{display:block;text-align:left;width:100%;background:#f5f3ff;color:#32276c;border:0;border-radius:8px;padding:10px;margin:5px 0;cursor:pointer;font:13px system-ui,sans-serif}button:hover{background:#e8e0ff}button.primary{color:#fff;background:#7762df}button.primary:hover{background:#6250c1}.msg{padding:9px;line-height:1.4;white-space:pre-wrap}";
    panel=document.createElement("div");
    panel.className="panel";
    shadow.append(style,panel);
    (document.body || document.documentElement).append(frame);
  }
  function position(anchor) {
    if (!frame || !visible(anchor)) return;
    const r=anchor.getBoundingClientRect();
    const left=Math.max(8,Math.min(innerWidth-318,r.left));
    frame.style.left=left+"px";
    frame.style.top=Math.max(8,Math.min(innerHeight-260,r.bottom+8))+"px";
  }
  function hide() {
    if(frame)frame.remove();
    frame=null;panel=null;shadow=null;
  }
  function line(text,classname="msg") {
    const div=document.createElement("div");
    div.className=classname;
    div.textContent=text;
    panel.append(div);
  }
  function button(text,action,primary=false) {
    const el=document.createElement("button");
    el.type="button";el.textContent=text;
    if(primary)el.className="primary";
    el.addEventListener("pointerdown",e=>e.preventDefault());
    el.addEventListener("click",e=>{e.stopPropagation();action();});
    panel.append(el);
  }
  function message(text) {if(!panel)return;panel.replaceChildren();line(text);}
  function assign(input,value) {
    if (!visible(input) || typeof value!=="string")return false;
    if(input.maxLength>=0 && input.maxLength<value.length)return false;
    const setter=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,"value")?.set;
    if(!setter)return false;
    setter.call(input,value);
    input.dispatchEvent(new Event("input",{bubbles:true}));
    input.dispatchEvent(new Event("change",{bubbles:true}));
    return true;
  }
  function liveDraft(ctx) {
    if (!draft)return false;
    if(draft.origin!==location.origin || draft.pass!==ctx.pass ||
       draft.pass.value!==draft.password || Date.now()-draft.created>5*60*1000) {
      draft=null;return false;
    }
    return true;
  }
  function title(ctx) {
    setup();
    panel.replaceChildren();
    line("🔐 BoshaVault","top");
    line(location.hostname,"sub");
    position(ctx.anchor);
  }
  async function fill(ctx,id) {
    const at=origin;
    message("Confirm this login in the BoshaVault Windows window…");
    try {
      const answer=await chrome.runtime.sendMessage({op:"fill",entryId:id});
      if(at!==location.origin || focus?.pass!==ctx.pass || !ctx.pass.isConnected ||
         !safeFormDestination(ctx.form)) {hide();return;}
      if(answer?.status!=="filled"){message(answer?.message || "Fill canceled.");return;}
      if(ctx.user?.isConnected) assign(ctx.user,answer.username);
      assign(ctx.pass,answer.password);
      hide();
    } catch {message("BoshaVault did not respond. Please retry.");}
  }
  function generate(ctx,length,symbols) {
    if (!visible(ctx.pass) || !safeFormDestination(ctx.form)) {hide();return;}
    const allowed=ctx.pass.maxLength>=0?ctx.pass.maxLength:64;
    const actual=Math.min(length,allowed);
    if(actual<16) {
      message("This page limits passwords to fewer than 16 characters. BoshaVault won't generate a weak password.");
      return;
    }
    // Only the deliberate button click creates a secret.
    const secret=BoshaPassword.generate(actual,symbols);
    if (!assign(ctx.pass,secret)) {message("This password field rejected the generated value.");return;}
    const confirmationFilled=ctx.confirm ? assign(ctx.confirm,secret):false;
    draft={pass:ctx.pass,password:secret,origin:location.origin,created:Date.now()};
    showDraft(ctx,confirmationFilled);
  }
  function showDraft(ctx,confirmationFilled) {
    title(ctx);
    line("Strong password generated and filled ("+draft.password.length+" characters)."+
      (ctx.confirm && !confirmationFilled?" Review the confirmation field.":"")+
      " Complete signup on the website separately.");
    const username=(ctx.user?.value||"").trim();
    line("Username/email: "+(username || "Not found here — enter it in Windows before saving."),"sub");
    button("Save username + password in BoshaVault",()=>save(ctx),true);
    button("Regenerate a new 24-character password",()=>generate(ctx,24,true));
    button("Close (keep values in form)",hide);
  }
  async function save(ctx) {
    if(!liveDraft(ctx)){message("The form changed. Focus the password field again to regenerate.");return;}
    if (!safeFormDestination(ctx.form)){message("This form submits to a different site. BoshaVault will not save it.");return;}
    if(location.origin!==origin || !ctx.pass.isConnected){hide();return;}
    const username=(ctx.user?.value||"").trim();
    const password=draft.password;
    message("Review the website and username in BoshaVault Windows, then choose Save encrypted login.");
    try {
      // Username and generated password leave the content script ONLY at this
      // explicit Save click. No background password-field reading or storage.
      const answer=await chrome.runtime.sendMessage({op:"save",username,password});
      if(location.origin!==origin || !ctx.pass.isConnected){hide();return;}
      if(answer?.status==="saved"){
        draft=null;
        message("Saved encrypted in BoshaVault. Submit the signup form on this website when ready.");
      }else if(answer?.status==="locked"){
        message("Unlock BoshaVault on Windows, then focus this field and click Save again.");
      }else{
        message(answer?.message || "Not saved. The password is still in the form — do not leave this page until you save it.");
      }
    } catch {message("Could not save. Keep this page open and retry after starting BoshaVault.");}
  }
  function render(ctx,reply) {
    if(!ctx.pass.isConnected || !focus || focus.pass!==ctx.pass || location.origin!==origin)return;
    if(liveDraft(ctx)){showDraft(ctx,false);return;}
    title(ctx);
    if(typeof reply?.warning==="string" && reply.warning.length>0)
      line("⚠️ "+reply.warning);
    if(reply?.status==="ok" && Array.isArray(reply.accounts)) {
      for(const account of reply.accounts.slice(0,10)){
        if(!account || typeof account.id!=="string" || typeof account.username!=="string")continue;
        button((account.title||"Saved login")+" · "+account.username,()=>fill(ctx,account.id));
      }
      if(reply.accounts.length)line("Creating a new account instead?","sub");
    } else if(reply?.status==="locked"){
      button("Unlock BoshaVault on Windows",async()=>{
        message("Unlock in BoshaVault, then focus the password field again.");
        await chrome.runtime.sendMessage({op:"open"}).catch(()=>{});
      });
    } else if(reply?.status==="unavailable"){
      line("Windows vault is disconnected. Generating still works, but saving needs BoshaVault running.");
    }
    button("Generate strong password · 24 characters",()=>generate(ctx,24,true),true);
    button("Generate extra-long password · 32 characters",()=>generate(ctx,32,true));
    button("Generate without symbols · 24 characters",()=>generate(ctx,24,false));
    position(ctx.anchor);
  }
  let debounce;
  document.addEventListener("focusin",event=>{
    // Clicking inside our shadow popup retargets to its host: don't close it.
    if(frame && (event.target===frame || frame.contains(event.target)))return;
    const ctx=credentialContext(event.target);
    if(!ctx){hide();focus=null;return;}
    focus=ctx;origin=location.origin;
    const ticket=++sequence;
    hide();clearTimeout(debounce);
    debounce=setTimeout(async()=>{
      let response;
      try {response=await chrome.runtime.sendMessage({op:"list"});}
      catch{response={status:"unavailable"};}
      if(ticket===sequence && focus===ctx && location.origin===origin)render(ctx,response);
    },140);
  },true);
  document.addEventListener("pointerdown",event=>{
    if(frame && !frame.contains(event.target) && !(event.target instanceof HTMLInputElement))hide();
  },true);
  window.addEventListener("scroll",()=>{if(frame&&focus)position(focus.anchor);},{passive:true});
  window.addEventListener("resize",()=>{if(frame&&focus)position(focus.anchor);});
  window.addEventListener("pagehide",()=>{draft=null;hide();});
})();