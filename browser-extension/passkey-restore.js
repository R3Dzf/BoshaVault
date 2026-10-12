/* Undo only Lockvyn's own optional conditional-passkey hook.
 * Other browser credential handlers and any site-owned overrides remain intact. */
(() => {
  "use strict";
  const container = navigator.credentials;
  if (!container) return;
  const key = Symbol.for("lockvyn.passkeys.conditional-only.v1");
  try {
    const state = container[key];
    if (!state) return;
    state.enabled = false;
    if (container.get === state.wrapper) {
      if (state.prior) {
        Object.defineProperty(container, "get", state.prior);
      } else {
        delete container.get;
      }
    }
    delete container[key];
  } catch {
    // If the page replaced an existing method, never overwrite that method.
    // Refreshing the login page returns Chrome's native behavior.
  }
})();
