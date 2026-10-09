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
        page.hostname !== tabUrl.hostname || !["list", "fill", "open", "save"].includes(message?.op)) {
      return {status: "denied"};
    }
    if (message.op === "fill" && !/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(message.entryId || "")) {
      return {status: "denied"};
    }
    if (message.op === "save" && (
      typeof message.username !== "string" || message.username.length > 2000 ||
      /[\r\n\0]/.test(message.username) ||
      typeof message.password !== "string" || message.password.length < 16 ||
      message.password.length > 128 || !/^[!-~]+$/.test(message.password))) {
      return {status:"denied", message:"Invalid generated password or username."};
    }
    if (["fill","save"].includes(message.op) && !(await sameActiveDocument(sender,page.origin)))
      return {status:"denied", message:"Browser page changed. Retry on the intended website."};
    const answer = await chrome.runtime.sendNativeMessage(NATIVE, {
      op: message.op, origin: page.origin, entryId: message.op === "fill" ? message.entryId : "",
      username: message.op === "save" ? message.username : "",
      password: message.op === "save" ? message.password : ""
    });
    if (!answer || typeof answer.status !== "string") return {status: "unavailable"};
    // Navigation during Windows confirmation must not deliver a secret to a
    // different page, even in the same tab.
    if ((message.op === "fill" && answer.status === "filled") ||
        (message.op === "save" && answer.status === "saved")) {
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
    status: "unavailable", message: "Connect the local BoshaVault Windows app and register the native host."
  }));
  return true;
});
