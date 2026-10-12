"use strict";

const NATIVE = "com.boshavault.desktop";
// The sender's live Chromium document ID is the security boundary. Tab URLs
// alone are insufficient when a tab navigates during a native approval dialog.
async function sameActiveDocument(sender, expectedOrigin) {
  if (!sender.tab?.id || sender.frameId !== 0 || !sender.documentId)
    return false;
  const frame = await chrome.webNavigation.getFrame({tabId:sender.tab.id,frameId:0});
  const tab = await chrome.tabs.get(sender.tab.id);
  if (!frame || frame.documentId !== sender.documentId || !tab.url)
    return false;
  if (tab.pendingUrl) return false;
  try {
    return new URL(frame.url).origin === expectedOrigin &&
      new URL(tab.url).origin === expectedOrigin;
  } catch { return false; }
}

chrome.runtime.onMessage.addListener((message, sender, respond) => {
  (async () => {
    if (!sender.tab || sender.frameId !== 0 || sender.id !== chrome.runtime.id) {
      return {status: "denied"};
    }
    const page = new URL(sender.url || sender.tab.url);
    const tabUrl = new URL(sender.tab.url);
    if (page.protocol !== "https:" || page.origin !== tabUrl.origin ||
        page.hostname !== tabUrl.hostname || !["list", "fill", "open", "save", "update", "capture"].includes(message?.op)) {
      return {status: "denied"};
    }
    if (["fill","update"].includes(message.op) && !/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(message.entryId || "")) {
      return {status: "denied"};
    }
    if (["save","update","capture"].includes(message.op) && (
      typeof message.username !== "string" || message.username.length > 2000 ||
      /[\r\n\0]/.test(message.username) ||
      (message.op !== "capture" && (typeof message.password !== "string" ||
      message.password.length < 16 || message.password.length > 128 || !/^[!-~]+$/.test(message.password))))) {
      return {status:"denied", message:"Invalid generated password or username."};
    }
    if (["list","fill","save","update","capture"].includes(message.op) && !(await sameActiveDocument(sender,page.origin)))
      return {status:"denied", message:"Browser page changed. Retry on the intended website."};
    const answer = await chrome.runtime.sendNativeMessage(NATIVE, {
      op: message.op, origin: page.origin, entryId: ["fill","update"].includes(message.op) ? message.entryId : "",
      username: ["save","update","capture"].includes(message.op) ? message.username : "",
      password: ["save","update"].includes(message.op) ? message.password : ""
    });
    if (!answer || typeof answer.status !== "string") return {status: "unavailable"};
    // An HTTPS tab may navigate while the native vault enumerates accounts.
    // Recheck the exact live document before returning username metadata,
    // not just before issuing the native request.
    if (message.op === "list" && !(await sameActiveDocument(sender,page.origin)))
      return {status:"denied",message:"The browser page changed. Reload and retry."};
    // Navigation during Windows confirmation must not deliver a secret to a
    // different page, even in the same tab.
    if ((message.op === "fill" && answer.status === "filled") ||
        (message.op === "save" && answer.status === "saved") ||
        (message.op === "update" && answer.status === "updated") ||
        (message.op === "capture" && answer.status === "saved")) {
      if (!(await sameActiveDocument(sender,page.origin))) {
        return {status:"denied",message:"The tab navigated during approval. Secret was not delivered."};
      }
      if (message.op === "fill" &&
          (typeof answer.username !== "string" || typeof answer.password !== "string")) {
        return {status: "denied"};
      }
    }
    return answer;
  })().then(respond).catch(() => respond({
    status: "unavailable", message: "Connect the local Lockvyn Windows app and register the native host."
  }));
  return true;
});


/* Chrome/Edge WebAuthn conditional UI control is opt-in and separate from
 * Lockvyn's Native Messaging channel. The page-world script has no access
 * to Chrome's extension API, local vault or encrypted credentials. */
const PASSKEY_SCRIPT_ID = "lockvyn-conditional-passkeys-v1";
let passkeyScriptQueue=Promise.resolve();
function queuePasskeyScriptSync(applyToFocusedTab=false){
  passkeyScriptQueue=passkeyScriptQueue.catch(()=>{}).then(async()=>{
    if(!chrome.scripting?.getRegisteredContentScripts)return;
    const prefs=await chrome.storage.local.get({blockConditionalPasskeys:false});
    const enabled=prefs.blockConditionalPasskeys===true;
    const existing=await chrome.scripting.getRegisteredContentScripts({ids:[PASSKEY_SCRIPT_ID]});
    if(enabled && !existing.length){
      await chrome.scripting.registerContentScripts([{
        id:PASSKEY_SCRIPT_ID,
        js:["passkey-conditional.js"],
        matches:["https://*/*"],
        runAt:"document_start",
        allFrames:false,
        world:"MAIN",
        persistAcrossSessions:true
      }]);
    } else if(!enabled && existing.length){
      await chrome.scripting.unregisterContentScripts({ids:[PASSKEY_SCRIPT_ID]});
    }
    if(!applyToFocusedTab || !chrome.tabs?.query || !chrome.scripting.executeScript)return;
    // Don't reload forms with unsaved data: apply immediately to just the
    // current active HTTPS page. Other open pages may need a manual refresh.
    const tabs=await chrome.tabs.query({active:true,currentWindow:true});
    const tab=tabs?.[0];
    if(!tab?.id || !tab.url?.startsWith("https://"))return;
    await chrome.scripting.executeScript({
      target:{tabId:tab.id,frameIds:[0]},
      files:[enabled?"passkey-conditional.js":"passkey-restore.js"],
      world:"MAIN",
      injectImmediately:true
    });
  });
  return passkeyScriptQueue;
}
chrome.runtime.onInstalled?.addListener?.(()=>void queuePasskeyScriptSync());
chrome.runtime.onStartup?.addListener?.(()=>void queuePasskeyScriptSync());
chrome.storage?.onChanged?.addListener?.((changes,area)=>{
  if(area==="local" && changes.blockConditionalPasskeys)
    void queuePasskeyScriptSync(true);
});
