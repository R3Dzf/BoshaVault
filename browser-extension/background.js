"use strict";

const NATIVE = "com.boshavault.desktop";
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  (async () => {
    if (!sender.tab || sender.frameId !== 0 || sender.id !== chrome.runtime.id) {
      return {status: "denied"};
    }
    const page = new URL(sender.url || sender.tab.url);
    const tabUrl = new URL(sender.tab.url);
    if (page.protocol !== "https:" || page.origin !== tabUrl.origin ||
        page.hostname !== tabUrl.hostname || !["list", "fill", "open"].includes(message?.op)) {
      return {status: "denied"};
    }
    if (message.op === "fill" && !/^[0-9a-f]{8}-[0-9a-f-]{27,}$/i.test(message.entryId || "")) {
      return {status: "denied"};
    }
    const answer = await chrome.runtime.sendNativeMessage(NATIVE, {
      op: message.op, origin: page.origin, entryId: message.op === "fill" ? message.entryId : ""
    });
    if (!answer || typeof answer.status !== "string") return {status: "unavailable"};
    // Navigation during Windows confirmation must not deliver a secret to a
    // different page, even in the same tab.
    if (message.op === "fill" && answer.status === "filled") {
      const tabNow = await chrome.tabs.get(sender.tab.id);
      if (!tabNow.url || new URL(tabNow.url).origin !== page.origin ||
          (sender.documentId && tabNow.documentId && sender.documentId !== tabNow.documentId)) {
        return {status: "denied"};
      }
      if (typeof answer.username !== "string" || typeof answer.password !== "string") {
        return {status: "denied"};
      }
    }
    return answer;
  })().then(respond).catch(() => respond({
    status: "unavailable", message: "Connect the local BoshaVault Windows app and register the native host."
  }));
  return true;
});
