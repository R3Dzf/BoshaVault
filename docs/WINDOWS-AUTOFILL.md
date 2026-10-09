# BoshaVault Windows browser Autofill (Chrome and Microsoft Edge)

> **Preview, not production audited.** Real Chrome/Edge device testing, extension packaging and an independent security audit are required before broad distribution. Windows builds now include the extension source, native host and a per-user registration script.

## What is in the ZIP

- `BoshaVault.exe`: Windows desktop vault app. **Run this to open/unlock the vault**.
- `BoshaVault.NativeHost.exe`: a command-line helper used only by Chrome or Edge through Native Messaging.
- `browser-extension/`: Chromium Manifest V3 extension to load in developer mode.
- `Install-BrowserAutofill.ps1`: registers the local native messaging helper for your browser/extension ID.

Do not install random extension clones or distribute a modified ZIP as production-ready. Only install code you review and trust. No credentials are stored by the extension.

## Installation (repeat for each browser)

1. Download the **BoshaVault-Windows-x64** GitHub Actions artifact from the latest successful build. Extract the ZIP **entirely** to a stable folder, for example `D:\BoshaVault-Windows`. Close any older BoshaVault process from the tray (Exit) before replacing the program.
2. In Chrome open `chrome://extensions`; in Edge open `edge://extensions`. Turn **Developer mode** on.
3. Select **Load unpacked** and choose the extracted `browser-extension` directory. The extension is **BoshaVault Autofill (Local Preview)**.
4. Copy its exact 32-character **Extension ID** shown on the browser extensions page. Different browsers may show different IDs.
5. Open PowerShell in the extracted app directory, and run one of these, substituting the real extension ID:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File ".\Install-BrowserAutofill.ps1" -Browser Chrome -ExtensionId "YOUR_32_CHARACTER_EXTENSION_ID"
   ```
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File ".\Install-BrowserAutofill.ps1" -Browser Edge -ExtensionId "YOUR_32_CHARACTER_EXTENSION_ID"
   ```
6. **Restart that browser completely** so it sees the native host registration. Do not move the extracted application directory afterward; the registration points to the existing `BoshaVault.NativeHost.exe` path.
7. Run `BoshaVault.exe` and unlock your vault. Under **Your vault settings**, keep **Enable local Chrome / Edge Autofill** checked.
8. Add a demo credential with website `https://github.com` and **fake** username/password. Go to `https://github.com/login` in the browser, focus the password or username field, and look for the BoshaVault inline dropdown near the field.
9. Click a matching login. The Windows app comes forward with **Confirm browser Autofill** displaying the exact HTTPS hostname and account. Choose **Yes** to send *that account* to the current browser tab. BoshaVault will fill the fields but will **not** submit the login form.

Do not try authenticating with the fake credentials; this tests form filling only. If the vault is locked, the extension shows **Unlock BoshaVault on Windows**, opens the app, and asks you to focus the browser field again after unlocking.

### Common reasons for no suggestion

- The extension isn't loaded/allowed for the website, or its ID was mistyped during registration.
- Browser needs restart after updating Native Messaging registration.
- `BoshaVault.exe` isn't running or vault is locked.
- No stored password entry matches the **exact** HTTPS host; `github.com` is deliberately different from `accounts.github.com`.
- The website has no ordinary visible password input, its fields are inside cross-origin frames or shadow DOM, or the content script does not recognize its login form.
- The browser disables extension scripts on privileged internal pages (e.g. `chrome://`, `edge://`), on HTTP pages or in restricted environments.
- On Incognito, you must explicitly allow the extension in the browser extension settings; first test in a normal tab.
- Native Messaging registration is under the **current Windows account only**; a different Windows user won't see it.
- Browser notifications may hide the dropdown; try clicking the password field again.

You can confirm the registration at `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.boshavault.desktop` or `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.boshavault.desktop`. Its JSON manifest is in `%LOCALAPPDATA%\BoshaVault\NativeMessaging\com.boshavault.desktop.json` and its allowed_origins array must contain the exact extension ID. Do **not** set wildcard origins.

## Security / limitations

- Nothing is auto-submitted. Passwords are released **only after a user click in the extension AND a separate explicit approval in the Windows app**. The extension cannot unlock the vault on its own.
- Local Named Pipe is scoped to the Windows user and interactive session. Browser-to-host communication uses Chrome/Edge's native messaging allowlist. Same-user malware could still target the local pipe or read process memory; the final consent prompt is intentionally mandatory, not an authentication boundary against malware controlling the desktop.
- Only normalized **exact HTTPS DNS hostnames** with no custom port are eligible. No suffix matching, no subdomain guesses, no HTTP, no embedded userinfo. Native app fills and Windows logon screens are **not** supported.
- Only names/titles/usernames for the matched site pass to the extension before user consent. A selected password is sent after confirmation, just long enough to fill the current form. Browser pages can then read their own filled values, as with every normal password manager.
- Extension host permissions target **all HTTPS websites** so it can recognize future login pages. It has no network APIs for uploading vault data. This broad permission still deserves review before a store release.
- Existing Chromium documents sometimes use shadow roots, login-iframes, multi-step sign-in and SPA navigation; support for those requires further device testing and hardened DOM tracking.
- The extension uses an in-page dropdown controlled by its isolated content script; there is no accessibility keylogger, keyboard hook, clipboard monitoring, auto-typing across windows, or credential server.
- Neither native host nor WPF server binds a network socket. An existing running Windows instance owns the decrypted vault; closing to tray clears it.

## Acceptance tests still needed

1. Chrome + Edge install/register/restart, with each browser's unique extension ID.
2. Unlock → focus field → see matching account → deny and confirm fill → verify correct fields and no automatic form submit.
3. Lock vault, close to tray, Windows workstation lock, browser/tab navigation mid-confirmation, and cancel flow.
4. Test unrelated domain, HTTP site, lookalike host, cross-origin iframe, and login form with no matching account. They must never receive a password.
5. Code signing, extension publishing and independent security assessment prior to claims of production readiness.

If installation prompts demand turning off Defender, disabling browser security protections, or sharing the master passphrase outside BoshaVault, **stop**. Those steps are not part of this setup.
