# Lockvyn Browser Autofill — Chrome / Edge and browser profiles

Lockvyn communicates through a local Windows Native Messaging host. Browser extensions **do not store the encrypted vault, master passphrase or session tokens**. Each credential fill requires review in the unlocked Windows application. Extension content scripts operate on HTTPS pages only, with document identity checks for navigation races.

## One-time setup (user-approved)

1. In Lockvyn Windows, open **Settings → Browser extension setup → Connect browser extension**.
2. Select **Open extension folder**, then open `chrome://extensions` or `edge://extensions` in each profile you choose to use.
3. Enable the browser's Developer mode, select **Load unpacked**, and choose Lockvyn's supplied `browser-extension` folder. This requires explicit approval per browser profile; the app does **not** edit Chrome profiles to force installation.
4. Copy the installed extension's 32-character ID from the browser and paste it into the **Connect browser extension** dialog in Lockvyn. Repeat with a second ID if another store/browser uses a different ID.
5. Restart that browser once. Matching saved logins are offered on supported HTTPS login fields. The Windows vault must be unlocked and approves individual fills.

The Windows Native Messaging registration is **per Windows user**, not per Chrome profile. That is why this one-time bridge supports all Chrome/Edge profiles where the extension was installed. On every subsequent Lockvyn launch after a Windows update, the app automatically repairs a previously approved host executable path if the updater moved it. It does **not** grant new extension IDs, register new browsers secretly or modify browser settings.

For technical/portable setups, `Install-BrowserAutofill.ps1 -Browser All -ExtensionId <ID>` performs the same user-approved native host setup for installed Chrome/Edge/Chromium. `-RepairOnly` restores a previously approved local host without adding new origins. The script never installs an extension or changes managed policies.

## Less intrusive browsing

Lockvyn Autofill **0.7.0** has an optional **Quiet Mode** in its toolbar popup. When enabled, focusing an HTTPS field does not automatically show a credential dropdown. Select **Show saved logins** from the extension menu when you actually want suggestions. The preference is local to the browser profile, with no usernames or passwords stored there. If you prefer, **unpin** the extension icon from Chrome/Edge's toolbar. Unpinning the icon does not disable its installed functionality.

Browser installation confirmation/security warnings cannot be hidden or bypassed. Never disable browser extension safety checks. Installing an extension once per browser profile is separate from maintaining the Native Messaging bridge.

## More browsers

The Chromium Manifest V3 code can be adapted for other Chromium-based browsers (including Brave and Vivaldi), but their native host registration and installation behavior require verification per browser. Opera documents a Chrome-compatible Native Messaging manifest but independent interoperability tests remain necessary. Firefox uses a different allowed-extension/native-host manifest and requires a separate tested extension package. No unsupported browser is silently configured by Lockvyn.

Run `Get-BrowserAutofillStatus.ps1` for a read-only local report of available Chrome, Edge, Brave, Vivaldi, Chromium and Opera profile directories and whether approved extension IDs were detected. Some unpacked extensions/browser forks may not be detectable; this is an advisory report, not an installation result. The script never writes to browser profiles.

## Why not automatically force-install it everywhere?

Chrome and Edge enforce extension installation choices. Supported automatic store updates happen after an extension is legitimately installed; forced enterprise installation policies are administrative controls that may prevent users from removing the extension. Lockvyn intentionally avoids those policies and the hidden alteration of browser user data. A published Chrome Web Store / Edge Add-ons extension would provide seamless ongoing extension updates; a store listing is not available yet, so local unpacked installs need a browser reload to pick up source changes.

### Security boundary

The native host manifest retains **only explicitly approved extension origins**; wildcard origins are rejected. Lockvyn's repair path never expands the allowlist, never reads user browser passwords/cookies/history, and keeps the encrypted vault in the existing Windows application. Don't include secrets or complete browser profile files in bug reports.

## Hiding the Chrome/Windows Hello passkey dropdown (experimental)

Lockvyn Autofill **0.8.0** includes an explicit, **off-by-default** switch in its extension popup:
**Hide automatic Passkey suggestions**.

When switched on, the extension installs a small JavaScript interception at the start of **new HTTPS page loads**, only for:
`navigator.credentials.get({publicKey: ..., mediation: "conditional"})`.
The site receives a standard `NotAllowedError` for those silent conditional requests, which can prevent its automatic WebAuthn/passkey suggestion list. A passkey sign-in initiated explicitly by clicking a website's sign-in button (without conditional mediation) remains available, as does passkey **creation**. No challenge, response, private key, password, cookie or account information is collected or sent.

Important limitations:
- This modifies a web API within a website's scripting context and **does not guarantee** suppression of Chrome's browser-owned credential menu, already-saved Chrome passwords, Windows Hello UI, or websites with other passkey mechanisms.
- Some login pages may fall back less gracefully after a conditional request is declined. Disable the switch on sites where passkey sign-in behaves unexpectedly.
- The script is shared with the website's JavaScript. It is a **convenience feature, not a tamper-proof security control**. A website can override web APIs or show its own login UI.
- On/off changes apply to the active HTTPS tab where possible and to future page loads; **reload other open login pages** after changing the setting. Browser-specific restricted pages and nested iframe login forms may not be affected.
- The extension never deletes stored passkeys or changes Chrome policies, browser password settings, Windows Hello settings, or Microsoft accounts.
- Because this feature manipulates WebAuthn page calls, browser updates or site changes may change the outcome. Always leave this option off when in doubt.

If the screenshot shows entries labeled `Passkey · Windows Hello` even when the experimental switch is on, record the browser version and exact site and report that the browser-native dropdown persists. Do not delete passkeys or turn off Windows Hello merely to hide UI.
