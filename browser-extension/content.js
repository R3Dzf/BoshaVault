"use strict";

// No background password-field harvesting: read username only after the user
// explicitly chooses Save, and only from this focused login form.
(() => {
  if (window.top !== window || location.protocol !== "https:") return;
  const pwSelector = 'input[type="password"],input[autocomplete="current-password"],input[autocomplete="new-password"]';
  let frame=null, shadow=null, panel=null, focus=null, sequence=0, draft=null, origin=location.origin;
  let savedMatches=[];
  // Only a local UI preference is stored by the extension. Never persist
  // usernames, passwords, unlock state, recovery material or native replies.
  let quietMode=false;
  chrome.storage?.local?.get?.({quietMode:false},prefs=>{
    quietMode=prefs?.quietMode===true;
    if(quietMode)hide();
  });
  chrome.storage?.onChanged?.addListener?.((changes,area)=>{
    if(area!=="local" || !changes.quietMode)return;
    quietMode=changes.quietMode.newValue===true;
    if(quietMode)hide();
  });

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
  function isUsername(input) {
    if (!(input instanceof HTMLInputElement) || !visible(input)) return false;
    if (!["text","email","tel",""].includes(input.type)) return false;
    const value=(input.autocomplete+" "+input.name+" "+input.id+" "+
      (input.getAttribute("aria-label")||"")).toLowerCase();
    return input.type==="email" || /\busername\b|\bemail\b|user|login|mail|identifier|account|phone/.test(value);
  }
  function credentialContext(input) {
    if (!visible(input)) return null;
    const form=input.form || input.closest("form") || document;
    if (!safeFormDestination(form)) return null;
    const pw=[...form.querySelectorAll(pwSelector)].filter(visible).filter(e=>e.type==="password");
    if(!pw.length && !isUsername(input))return null;
    const pass=pw.length ? (pw.find(e=>e.autocomplete==="new-password") ||
      (pw.includes(input)?input:pw[0])) : null;
    if (!pw.includes(input) && !isUsername(input))return null;
    const user=[...form.querySelectorAll('input:not([type="hidden"]):not([type="password"])')]
      .find(e=>isUsername(e)) || (isUsername(input)?input:null);
    const confirm=pass ? (pw.find(e=>e!==pass &&
      (e.autocomplete==="new-password" ||
       /confirm|repeat|verify|retype/i.test((e.name || "")+" "+(e.id || "")))) || null) : null;
    return {pass,user,confirm,anchor:input,form};
  }
  function setup() {
    if (frame) return;
    frame=document.createElement("div");
    frame.setAttribute("data-boshavault-ui","1");
    frame.style.cssText="position:fixed;z-index:2147483646;width:310px;max-width:calc(100vw - 16px);pointer-events:auto;";
    shadow=frame.attachShadow({mode:"closed"});
    const style=document.createElement("style");
    style.textContent=".panel{box-sizing:border-box;width:100%;background:#fff;color:#242335;border:1px solid #e1e5ef;border-radius:14px;box-shadow:0 6px 18px #171b2e1f;padding:10px;max-height:min(390px,70vh);overflow-y:auto;scrollbar-width:thin;scrollbar-color:#b9abea transparent;overscroll-behavior:contain;font:13px system-ui,sans-serif}.top{font-weight:700;padding:5px;color:#6558d9}.sub{font-size:11px;color:#777;overflow-wrap:anywhere;padding:0 6px 7px}button{display:block;text-align:left;width:100%;background:#efedff;color:#32276c;border:0;border-radius:8px;padding:10px;margin:5px 0;cursor:pointer;font:13px system-ui,sans-serif}button:hover{background:#e8e0ff}button.primary{color:#fff;background:#6558d9}button.primary:hover{background:#6250c1}.msg{padding:9px;line-height:1.4;white-space:pre-wrap}";
    style.textContent += ".panel::-webkit-scrollbar{width:6px}.panel::-webkit-scrollbar-track{background:transparent}.panel::-webkit-scrollbar-thumb{background:#b9abea;border-radius:10px}.account-search{box-sizing:border-box;width:100%;height:39px;background:#faf9ff;color:#242335;border:1px solid #d8d2ed;border-radius:9px;padding:8px 11px;font:13px system-ui,sans-serif;outline:none}.account-search:focus{border-color:#6558d9}.section{font-size:11px;color:#70698a;padding:9px 6px 3px;font-weight:600}";
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
  function button(text,action,primary=false,container=panel) {
    const el=document.createElement("button");
    el.type="button";el.textContent=text;
    if(primary)el.className="primary";
    el.addEventListener("pointerdown",e=>e.preventDefault());
    el.addEventListener("click",e=>{e.stopPropagation();action();});
    container.append(el);
    return el;
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
    line("🔐 Lockvyn","top");
    line(location.hostname,"sub");
    position(ctx.anchor);
  }
  async function fill(ctx,id) {
    const at=origin;
    message("Approve the small Lockvyn confirmation, without reopening the full vault…");
    try {
      const answer=await chrome.runtime.sendMessage({op:"fill",entryId:id});
      if(at!==location.origin || focus?.anchor!==ctx.anchor ||
       !(ctx.pass?.isConnected || ctx.user?.isConnected) ||
       !safeFormDestination(ctx.form)) {hide();return;}
      if(answer?.status!=="filled"){message(answer?.message || "Fill canceled.");return;}
      if(ctx.user?.isConnected) assign(ctx.user,answer.username);
      if(ctx.pass?.isConnected) {
        if(!assign(ctx.pass,answer.password)){
          message("Site password field rejected the password. Use Lockvyn's Copy password as a fallback.");
          return;
        }
        hide();
      }else {
        message("Username filled. Continue to the password step and select the same account again.");
      }
    } catch {message("Lockvyn did not respond. Please retry.");}
  }
  function generate(ctx,length,symbols) {
    if (!visible(ctx.pass) || !safeFormDestination(ctx.form)) {hide();return;}
    const allowed=ctx.pass.maxLength>=0?ctx.pass.maxLength:64;
    const actual=Math.min(length,allowed);
    if(actual<16) {
      message("This page limits passwords to fewer than 16 characters. Lockvyn won't generate a weak password.");
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
    button("Save username + password in Lockvyn",()=>save(ctx),true);
    for(const entry of savedMatches.filter(e=>!e.related).slice(0,5)) {
      if(typeof entry.id!=="string" || typeof entry.username!=="string")continue;
      button("Update saved password · "+entry.username,()=>updatePassword(ctx,entry.id));
    }
    button("Regenerate a new 24-character password",()=>generate(ctx,24,true));
    button("Close (keep values in form)",hide);
  }
  async function updatePassword(ctx,id) {
    if(!liveDraft(ctx) || !safeFormDestination(ctx.form)) {
      message("The page or password changed. Focus the field and regenerate.");return;
    }
    const sentOrigin=location.origin;
    const username=(ctx.user?.value||"").trim();
    message("Review the saved account and new password change in Lockvyn Windows…");
    try {
      const answer=await chrome.runtime.sendMessage({
        op:"update",entryId:id,username,password:draft.password
      });
      if(location.origin!==sentOrigin || !ctx.pass.isConnected){hide();return;}
      if(answer?.status==="updated") {
        draft=null;
        message("Updated the encrypted saved login. Verify the new password works on the website.");
      }else {
        message(answer?.message || "Password update was not saved. Review the website and retry.");
      }
    } catch {
      message("Update could not be completed. Keep the form open, verify the website, and try again.");
    }
  }
  async function save(ctx) {
    if(!liveDraft(ctx)){message("The form changed. Focus the password field again to regenerate.");return;}
    if (!safeFormDestination(ctx.form)){message("This form submits to a different site. Lockvyn will not save it.");return;}
    if(location.origin!==origin || !ctx.pass.isConnected){hide();return;}
    const username=(ctx.user?.value||"").trim();
    const password=draft.password;
    message("Review the website and username in Lockvyn Windows, then choose Save encrypted login.");
    try {
      // Username and generated password leave the content script ONLY at this
      // explicit Save click. No background password-field reading or storage.
      const answer=await chrome.runtime.sendMessage({op:"save",username,password});
      if(location.origin!==origin || !ctx.pass.isConnected){hide();return;}
      if(answer?.status==="saved"){
        draft=null;
        message("Saved encrypted in Lockvyn. Submit the signup form on this website when ready.");
      }else if(answer?.status==="locked"){
        message("Unlock Lockvyn on Windows, then focus this field and click Save again.");
      }else{
        message(answer?.message || "Not saved. The password is still in the form — do not leave this page until you save it.");
      }
    } catch {message("Could not save. Keep this page open and retry after starting Lockvyn.");}
  }
  function render(ctx,reply) {
    if(!(ctx.pass?.isConnected || ctx.user?.isConnected) ||
       !focus || focus.anchor!==ctx.anchor || location.origin!==origin)return;
    if(ctx.pass && liveDraft(ctx)){showDraft(ctx,false);return;}
    title(ctx);
    if(typeof reply?.warning==="string" && reply.warning.length>0)
      line("⚠️ "+reply.warning);
    savedMatches=reply?.status==="ok" && Array.isArray(reply.accounts)
      ? reply.accounts.slice(0,50) : [];
    const more=document.createElement("div");
    more.style.display="none";
    let extraCount=0;
    if(reply?.status==="ok" && Array.isArray(reply.accounts)) {
      const accounts=reply.accounts.filter(a=>a && typeof a.id==="string" &&
        typeof a.username==="string").slice(0,50);
      if(accounts.length) {
        const search=document.createElement("input");
        search.type="search";search.className="account-search";
        search.placeholder="Search email or account…";
        search.setAttribute("aria-label","Search saved login accounts");
        panel.append(search);
        const entries=document.createElement("div");
        panel.append(entries);
        const noResults=document.createElement("div");
        noResults.className="sub";noResults.textContent="No matching saved account.";
        noResults.style.display="none";panel.append(noResults);
        const normalizedEmail=(ctx.user?.value||"").trim().toLocaleLowerCase();
        accounts.sort((a,b)=>{
          const score=x=>x.username.toLocaleLowerCase()===normalizedEmail && normalizedEmail ? -1 :
            x.related!==true ? 0 : x.groupMatched===true ? 1 : 2;
          return score(a)-score(b);
        });
        const rows=[];
        let lastSection="";
        for(const account of accounts){
          const section=account.related!==true ? "Exact website" :
            account.groupMatched===true ? "Your trusted domain group" : "Related website · review before filling";
          if(section!==lastSection){
            const heading=document.createElement("div");heading.className="section";
            heading.textContent=section;entries.append(heading);
            rows.push({element:heading,key:"",isHeading:true});
            lastSection=section;
          }
          const savedHost=typeof account.website==="string"?account.website:"";
          const exact=account.related!==true;
          const entryLabel=(account.title||"Saved login")+" · "+account.username+
            (exact?"":" · "+savedHost);
          const el=button(entryLabel,()=>fill(ctx,account.id),exact,entries);
          rows.push({element:el,key:(account.username+" "+(account.title||"")+" "+savedHost).toLocaleLowerCase()});
        }
        search.addEventListener("input",()=>{
          const q=search.value.toLocaleLowerCase().trim();
          let count=0;
          for(const row of rows){
            if(row.isHeading)continue;
            const show=row.key.includes(q);row.element.style.display=show?"block":"none";
            if(show)count++;
          }
          // Hide an entire empty section after a filter has been applied.
          let heading=null,shown=0;
          for(const row of [...rows,{isHeading:true,element:null}]){
            if(row.isHeading){
              if(heading)heading.style.display=shown?"block":"none";
              heading=row.element;shown=0;
            }else if(row.element.style.display!=="none")shown++;
          }
          noResults.style.display=count?"none":"block";
        });
      }else line("No saved login matched this website.","sub");
    } else if(reply?.status==="locked") {
      button("Unlock Lockvyn",async()=>{
        message("Unlock your vault, then select the account here.");
        await chrome.runtime.sendMessage({op:"open"}).catch(()=>{});
      },true);
    } else if(reply?.status==="unavailable"){
      line("Lockvyn on Windows is not connected.","sub");
    }
    button("Save current website",()=>capture(ctx),false,more);extraCount++;
    if(ctx.pass) {
      button("Generate strong password",()=>generate(ctx,24,true),!savedMatches.length,
        savedMatches.length?more:panel);
      if(savedMatches.length)extraCount++;
      button("Generate 32-character password",()=>generate(ctx,32,true),false,more);
      button("Generate without symbols",()=>generate(ctx,24,false),false,more);
      extraCount+=2;
    }
    if(extraCount){
      button("More options ▾",()=>{
        more.style.display=more.style.display==="none"?"block":"none";
      });
      panel.append(more);
    }
    position(ctx.anchor);
  }
  async function capture(ctx) {
    if(!safeFormDestination(ctx.form)) {message("This page form targets another origin. Saving is blocked.");return;}
    const previousOrigin=origin;
    const username=(ctx.user?.isConnected?ctx.user.value:"").trim();
    message("Lockvyn Windows will open a prefilled login form. Review the exact site before saving.");
    try {
      const reply=await chrome.runtime.sendMessage({op:"capture",username});
      if(location.origin!==previousOrigin || !ctx.anchor.isConnected){hide();return;}
      message(reply?.message || "Finish adding the login in Lockvyn Windows.");
    }catch {message("Could not connect to Lockvyn Windows. Check that it is open and unlocked.");}
  }
  let debounce;
  function showForInput(input) {
    const ctx=credentialContext(input);
    if(!ctx){hide();focus=null;return false;}
    focus=ctx;origin=location.origin;
    const ticket=++sequence;
    hide();clearTimeout(debounce);
    debounce=setTimeout(async()=>{
      let response;
      try {response=await chrome.runtime.sendMessage({op:"list"});}
      catch{response={status:"unavailable"};}
      if(ticket===sequence && focus===ctx && location.origin===origin)render(ctx,response);
    },100);
    return true;
  }
  document.addEventListener("focusin",event=>{
    if(frame && (event.target===frame || frame.contains(event.target)))return;
    if(!quietMode)showForInput(event.target);
  },true);
  chrome.runtime.onMessage.addListener((message,_sender,reply)=>{
    if(message?.op==="showSuggestions"){
      const candidates=[document.activeElement,
        ...document.querySelectorAll('input[type="password"],input[type="email"],input[type="text"],input[type="tel"]')];
      const candidate=candidates.find(el=>credentialContext(el));
      if(!candidate){reply({status:"no-fields"});return;}
      showForInput(candidate);
      reply({status:"ok"});
      return;
    }
    if(message?.op==="captureSite") {
      // Only the user-initiated extension popup sends this request.
      const usernameInput=[...document.querySelectorAll("input")].find(isUsername);
      const username=(usernameInput?.value||"").trim();
      // Reply before Windows opens a modal: the browser action popup closes
      // when the user switches to Windows, but the content script remains alive.
      reply({status:"started"});
      chrome.runtime.sendMessage({op:"capture",username})
        .then(response=>{
          if(response?.status!=="saved") {
            // No credentials are included in this UI status.
            const ctx=credentialContext(document.activeElement);
            if(ctx){title(ctx);message(response?.message||"Site capture was canceled.");}
          }
        })
        .catch(()=>{});
      return;
    }
  });
  document.addEventListener("pointerdown",event=>{
    // Closed Shadow DOM retargets popup clicks to the host element. Hiding on
    // host pointerdown removed the buttons before the click handler could run.
    if(frame && event.target!==frame && !frame.contains(event.target) &&
       !(event.target instanceof HTMLInputElement))hide();
  },true);
  window.addEventListener("scroll",()=>{if(frame&&focus)position(focus.anchor);},{passive:true});
  window.addEventListener("resize",()=>{if(frame&&focus)position(focus.anchor);});
  window.addEventListener("pagehide",()=>{draft=null;hide();});
})();