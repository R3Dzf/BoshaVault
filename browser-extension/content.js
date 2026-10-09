"use strict";

// Runs only in the extension's isolated world on top-level HTTPS pages.
(() => {
  const selector = 'input[type="password"],input[autocomplete="current-password"]';
  let frame = null, root = null, panel = null, focus = null, seq = 0;
  let current = [], origin = location.origin;
  if (window.top !== window || location.protocol !== "https:") return;

  function shown(input) {
    if (!input || !input.isConnected || input.disabled || input.readOnly) return false;
    const r = input.getBoundingClientRect();
    return r.width > 12 && r.height > 9 && getComputedStyle(input).visibility !== "hidden";
  }
  function credentialContext(input) {
    if (!(input instanceof HTMLInputElement)) return null;
    const form = input.form || input.closest('form') || document;
    const pass = [...form.querySelectorAll(selector)].find(shown);
    if (!pass) return null;
    if (input !== pass && !['text','email',''].includes(input.type)) return null;
    const user = [...form.querySelectorAll('input:not([type="hidden"]):not([type="password"])')]
      .filter(shown).find(e => e.autocomplete === 'username' || /user|login|email/i.test(e.name || e.id) || e.type === 'email')
      || (input !== pass ? input : null);
    return {pass, user, anchor:input};
  }
  function setup() {
    if (frame) return;
    frame = document.createElement("div");
    frame.setAttribute("data-boshavault-ui", "1");
    frame.style.cssText = "position:fixed;z-index:2147483646;top:0;left:0;width:0;height:0;pointer-events:none";
    root = frame.attachShadow({mode:"closed"});
    const style = document.createElement("style");
    style.textContent = ".panel{width:300px;background:#fff;color:#242335;border:1px solid #dcd5f9;border-radius:12px;box-shadow:0 9px 26px #18122a3b;padding:9px;font:13px system-ui,sans-serif;pointer-events:auto}button{display:block;text-align:left;background:#f5f3ff;color:#32276c;border:0;border-radius:8px;padding:11px;width:100%;margin:4px 0;cursor:pointer;font:13px system-ui,sans-serif}button:hover{background:#e8e0ff}.top{font-weight:700;padding:6px;color:#7762df}.sub{font-size:11px;color:#777;overflow-wrap:anywhere;padding:0 6px 5px}.msg{padding:10px;line-height:1.4}";
    root.append(style);
    panel = document.createElement("div");
    panel.className = "panel"; root.append(panel);
    (document.body || document.documentElement).append(frame);
  }
  function place(input) {
    if (!frame || !shown(input)) return;
    const r = input.getBoundingClientRect();
    const x = Math.max(8,Math.min(window.innerWidth-310,r.left));
    const y = r.bottom + 8 + 160 > innerHeight ? Math.max(4,r.top-175):r.bottom+8;
    frame.style.left = x+"px"; frame.style.top = y+"px";
  }
  function hide() { if (frame) {frame.remove();frame=null;root=null;panel=null;} }
  function row(text, action) {
    const btn=document.createElement("button");
    btn.type="button";btn.textContent=text;
    btn.addEventListener("pointerdown",e=>e.preventDefault());
    btn.addEventListener("click",e=>{e.stopPropagation();action();});
    panel.append(btn);
  }
  function render(context, reply) {
    if (!context?.pass?.isConnected || focus !== context || location.origin !== origin) return;
    if (reply.status !== "locked" && (reply.status !== "ok" || !reply.accounts?.length)) {hide();return;}
    setup();panel.replaceChildren();
    const title=document.createElement("div");title.className="top";title.textContent="🔐 BoshaVault";panel.append(title);
    const domain=document.createElement("div");domain.className="sub";domain.textContent=location.hostname;panel.append(domain);
    if (reply.status === "locked") {
      row("Unlock BoshaVault on Windows",async()=>{
        rowMessage("Open BoshaVault, unlock it, then focus this field again.");
        await chrome.runtime.sendMessage({op:"open"}).catch(()=>{});
      });
      return;
    }
    current=reply.accounts.slice(0,10);
    for(const account of current) {
      if (!account?.id || typeof account.username !== "string") continue;
      row((account.title||"Saved login")+" · "+account.username,()=>fill(context,account.id));
    }
    place(context.anchor);
  }
  function rowMessage(message) {
    if (!panel) return;
    panel.replaceChildren();const div=document.createElement("div");div.className="msg";div.textContent=message;panel.append(div);
  }
  function assign(el,value) {
    if (!el || !shown(el) || typeof value !== "string") return;
    const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,"value")?.set;
    if (!setter) return;
    setter.call(el,value);
    el.dispatchEvent(new Event("input",{bubbles:true}));
    el.dispatchEvent(new Event("change",{bubbles:true}));
  }
  async function fill(context,id) {
    const at=origin;
    rowMessage("Approve this fill in the BoshaVault Windows window…");
    try {
      const reply=await chrome.runtime.sendMessage({op:"fill",entryId:id});
      // Verify the same live form and origin after foreground Windows approval.
      if (at !== location.origin || !context.pass.isConnected || focus !== context) {hide();return;}
      if (reply?.status !== "filled") {
        rowMessage(reply?.message || "Fill canceled. Focus the field again to retry.");
        return;
      }
      if (context.user?.isConnected) assign(context.user,reply.username);
      assign(context.pass,reply.password);
      hide();
    } catch {rowMessage("BoshaVault did not respond. Reopen the app and retry.");}
  }
  let debounce;
  document.addEventListener("focusin",e=>{
    const ctx=credentialContext(e.target);
    if (!ctx) {hide();focus=null;return;}
    focus=ctx; origin=location.origin;
    const ticket=++seq;
    hide();
    clearTimeout(debounce);
    debounce=setTimeout(async()=>{
      try {
        const reply=await chrome.runtime.sendMessage({op:"list"});
        if(ticket===seq && focus===ctx && location.origin===origin) render(ctx,reply);
      }catch { /* Do not inject into unsupported or disconnected pages. */ }
    },180);
  },true);
  document.addEventListener("pointerdown",e=>{
    if(frame && !frame.contains(e.target) && !(e.target instanceof HTMLInputElement))hide();
  },true);
  window.addEventListener("scroll",()=>{if(frame&&focus)place(focus.anchor);},{passive:true});
  window.addEventListener("resize",()=>{if(frame&&focus)place(focus.anchor);});
  window.addEventListener("pagehide",hide);
})();
