/* Lockvyn — optional, opt-in page-world passkey suggestion suppression.
 * Runs at document_start in MAIN only when the user has enabled it.
 * Never reads WebAuthn challenges, passkeys, authenticator responses or passwords.
 * Explicit user-initiated WebAuthn requests and passkey creation are untouched.
 *
 * Web pages share this JS world and can replace this hook; this is a UI
 * preference, NOT a security boundary or a guarantee that browser-owned
 * suggestions / native Windows Hello prompts will disappear.
 */
(() => {
  "use strict";
  const container = navigator.credentials;
  if (!container || typeof container.get !== "function") return;
  const key = Symbol.for("lockvyn.passkeys.conditional-only.v1");
  try {
    const previous = container[key];
    if (previous && previous.wrapper === container.get) {
      previous.enabled = true;
      return;
    }
    const prior = Object.getOwnPropertyDescriptor(container, "get");
    if (prior && prior.configurable === false) return;
    const nativeGet = container.get;
    const state = {enabled:true,nativeGet,prior,wrapper:null};
    const wrapper = function (...args) {
      const request = args[0];
      if (state.enabled && request?.mediation === "conditional" && request.publicKey != null) {
        return Promise.reject(new DOMException(
          "Automatic passkey suggestions are disabled in Lockvyn.",
          "NotAllowedError"));
      }
      return Reflect.apply(nativeGet, this, args);
    };
    state.wrapper = wrapper;
    Object.defineProperty(container, "get", {
      configurable:true, writable:true, value:wrapper
    });
    Object.defineProperty(container, key, {
      configurable:true, value:state
    });
  } catch {
    // A website or browser can protect its CredentialContainer object.
    // No fallback to intercepting credential data or changing other APIs.
  }
})();
