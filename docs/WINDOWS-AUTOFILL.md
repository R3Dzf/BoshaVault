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

## Generate a new password on a signup page

The Chrome and Edge extension now includes **Generate & Save (preview)**.

1. Open an ordinary HTTPS registration form and focus its password field. The in-page BoshaVault panel appears even if there are no matching saved logins.
2. Choose **Generate strong password · 24 characters**, **32 characters**, or **24 characters without symbols**. It uses the browser's cryptographic random source with rejection sampling and a shuffled mix of lower/upper-case letters and numbers (plus symbols when selected).
3. The chosen password goes into the registration password field. A confirmation field is filled only when its form metadata explicitly identifies it as a new-password/repeat/confirm field. The extension does not overwrite an identified current-password field when a new-password field exists.
4. The extension shows the detected username/email from the *same form*. Type it into the form if missing. It is captured **only when you explicitly click Save**, not continuously stored or monitored.
5. Click **Save username + password in BoshaVault**. Windows comes forward with **Save generated login**, showing the exact HTTPS website, an editable login name and editable username/email. Password stays masked. Confirm **Save encrypted login** to add a new entry. Existing credentials with the same username+website are not replaced or silently duplicated.
6. Complete the actual signup on the website separately. BoshaVault never presses a signup/login/submit button by itself.

If the Windows vault is locked, the extension can still generate and fill locally, but **cannot save** until you unlock it. Keep the page open and do not submit/leave the form before verifying that BoshaVault says the login was saved. No generated password or detected username is put into the clipboard, localStorage, extension storage or cloud. The live webpage can, as usual, read values explicitly filled into its own form.

### Safeguards / limitations

- We do **not** assume every password field is for signup. The generator is offered as a user-initiated action, never executed automatically.
- The site may reject special characters or impose maximum password lengths. The extension obeys a supported maximum of at least 16; when the site allows fewer than 16 characters, the generator refuses instead of silently weakening its default policy.
- Username detection is based on explicit form hints and common native field names. Multi-step signup flows may need you to enter the username manually in the Windows review dialog.
- Complex SPAs, iframe login forms and custom shadow-DOM fields are not guaranteed to work. Chrome/Edge + real websites need device testing before production use.
- A local process running under your Windows identity might forge native pipe requests; **Windows prompts for confirmation before saving or releasing a credential**. Review the hostname each time. This does not make a compromised computer safe.
- Encryption of stored data is handled by the existing encrypted BoshaVault vault (Argon2id for passphrase derivation and AES-GCM for vault data). Generating a password never creates a new encryption algorithm.

## Windows 1.5: safer browser filling and changing existing passwords

- A generic warning appears on domains that differ by a single character from one of your saved hosts, extend a saved hostname as a false prefix, or contain punycode labels. This warning is a **heuristic**. It cannot identify all phishing sites, and the browser page does not get the name of the saved account being compared.
- Credential replies now require the **same top-level Chromium document ID** before and after Windows approval. Navigation during a confirmation results in a refusal, not a secret delivered to a different tab document.
- The extension refuses ordinary HTML forms whose action or explicitly configured submitter target is a different origin. A website can still submit with JavaScript, so this does **not** prove a page is safe. Always check your address bar.
- On an existing saved website, generate a new password and choose **Update saved password** beside the matching account. BoshaVault Windows asks for explicit confirmation before replacing the old encrypted entry. It never submits a website form or guarantees the remote site accepted the new password. Confirm it works after the change; consider keeping a restorable encrypted backup.
- The extension needs the Chromium \`webNavigation\` permission to check document identity. Restart/reload the extension after upgrading, and recheck that its Native Messaging registration still points to the right extension ID.

## Import Chrome/Bitwarden CSV into the Windows vault

Open BoshaVault **Your vault settings → Import Chrome / Bitwarden passwords from CSV**, choose your export type and CSV, review the import report and an initial list of account names, then explicitly confirm **Import into encrypted vault**. A single authenticated encrypted write imports the entries; it does not replace your original data if validation fails.

Important:
- The CSV is **plaintext**, and the importer leaves it on disk. Save an encrypted vault backup first and handle the CSV afterward. Even file deletion may not securely erase an SSD or backups.
- A UTF-8 file must be no larger than **2 MB**, at most **2,000 records**, with recognized Chrome or Bitwarden headers and well-formed RFC-style CSV quoting.
- The importer never automatically overwrites accounts with the same exact HTTPS host and username. Insecure HTTP/custom-port sites, missing passwords, invalid fields and non-login items are skipped and counted.
- **Bitwarden items with TOTP secrets or custom fields are refused** to prevent silent data loss. Import just login/password records after separating those extra fields safely, or wait for a future complete encrypted TOTP integration.
- Browser-export notes, titles, folder labels and passwords enter the currently unlocked encrypted vault only after final confirmation. No uploads, secret logs, or clipboard steps.
- Existing **local Wi-Fi transfer remains unchanged**; imported entries can sync using the encrypted local transfer/merge once verified.

## Windows 1.6: Autofill usability and functional click fix

Previous browser suggestions could disappear before their click handlers executed: a closed Shadow DOM retargeted \`pointerdown\` events to the suggestion host, which was incorrectly treated as an outside click. Version 1.6 fixes this and includes a simulated Chrome content-script regression test that actually clicks a matching login suggestion and checks both fields.

The extension can now:
- Offer matching saved accounts on **username/email-only first steps** (when the password field is not yet on the page). Choose an account to fill its username; on the next password page you may need to select the account again.
- Treat **exact HTTPS host** and its optional literal \`www.\` prefix as matching, but **not** arbitrary subdomains: \`accounts.example.com\` is not interchangeable with \`example.com\`. Always review which site you approved.
- Show a clear no-matching-logins explanation, instead of displaying only generator buttons.
- Use the **extension toolbar icon** → **Show saved logins**, or **Save this website in BoshaVault** to open a Windows Add Login dialog with the browser-reported website and detected username/email automatically filled. This is the supported way to avoid typing the website manually. Save still requires unlocked vault, explicit action, and Windows review.
- Work with ordinary login forms on HTTPS pages only. Cross-origin iframe forms, sites controlled entirely by scripts/shadow-DOM widgets, HTTP, restricted Chrome/Edge pages and arbitrary Windows desktop apps are not promised.

Windows **Add a new login** now has a compact, screen-bounded, scrollable dialog with a permanently visible **Save login** and **Cancel** footer; **Website** is near the top and automatically suggests a name. If entering a login manually, you can type \`github.com\` and it is normalized to \`https://github.com/\`. **Paste website URL** reads the clipboard only after you click it, validates HTTPS, and never accepts a non-HTTPS URL. Generated passwords have a nearby **Copy password** button, using the app's existing 20-second best-effort clipboard clearing.

### Upgrading correctly

1. Close the old BoshaVault instance via the tray **Exit** (just clicking X only locks/hides it). Keep a tested encrypted backup.
2. Extract the complete latest \`BoshaVault-Windows-x64\` archive into a stable folder. Do not replace or delete \`%LOCALAPPDATA%\BoshaVault\vault.boshavault\`.
3. In \`chrome://extensions\` / \`edge://extensions\`, find **BoshaVault Autofill**, choose **Reload**, and check its access for the intended website. **Restart the browser** if the native host registration was changed.
4. If you moved the Windows folder, rerun the matching \`Install-BrowserAutofill.ps1\` registration using that browser's exact extension ID.
5. Open/unlock BoshaVault Windows and ensure **Enable local Chrome / Edge Autofill** is turned on. Add a **fake** login for \`https://github.com\`; focus the username/password field at \`https://github.com/login\`.
6. Confirm the saved account appears, click it, approve the one-time fill in BoshaVault Windows, and check the username/password fields are populated. Do **not** sign in with your fake credentials.
7. If the browser popup says the app is disconnected, verify that your system-tray BoshaVault process is the new build (not an old hidden instance), and that Native Messaging registration points to the new install path.

If a login is saved for a genuinely different host (for example \`accounts.example.com\` rather than \`example.com\`), edit the saved Website entry to the actual login host; do not disable phishing checks or allow substring matching to force a suggestion.

**Testing status:** GitHub automated tests cover expected behavior, but browser compatibility and real device/UI acceptance must still be verified. The extension is a security-sensitive preview, not an independently audited password manager.



## Windows 1.7 — readable fields and safe related-domain suggestions

- The compact Add / Edit Login form now uses legible 15px text, at least 50 logical pixels for single-line entries, and a higher multiline notes field. WPF's TextBox/PasswordBox templates now place padding on the border only instead of trimming the text inside a too-short scroll viewer. **Save** and **Cancel** remain visible in the sticky footer. Display scaling and font metrics may vary; verify the editor on real Windows hardware.
- The Autofill host distinguishes **Exact** and **Related domain** accounts. Exact includes identical HTTPS hostnames and the literal `www.` alias. Related uses the offline, private-inclusive Public Suffix List to find one registrable domain shared by different subdomains.
- Example: `https://www.google.com` saved login is marked **Related domain** at `https://myaccount.google.com`. Another example is `accounts.example.co.uk` and `shop.example.co.uk`; unlike naïve two-label matching, only the correct registrable domain `example.co.uk` is shared.
- The Public Suffix List includes PRIVATE rules, so `alice.github.io` and `bob.github.io`, or independent `blogspot.com` tenants, are **never related**, even though their ending looks similar. Lookalikes such as `google.com.attacker.test`, `lookalike-google.com`, mixed schemes and unrelated origins are rejected. If the offline rules list cannot be loaded, **related matching is disabled**, while exact matching remains available.
- Related-account suggestions show the saved hostname in the extension UI. Choosing one **does not silently autofill**. Windows opens a stronger confirmation identifying **BOTH** the page domain and original saved domain; choose Yes only after verifying both addresses. A compromised sibling subdomain could read any password that you intentionally fill there, so never accept this warning without checking. **Updating** an existing password still requires an exact saved website match.
- Related websites do not imply identity-provider cross-site federation. Login forms on `youtube.com` and `google.com` may use the same Google account, but they are not automatically related by PSL alone; filling across entirely different registrable domains requires an explicit separately designed linking feature.

The offline PSL is vendored at `src/BoshaVault.Core/Assets/public_suffix_list.dat` (Mozilla Public License v2.0), pinned to upstream publicsuffix/list commit `3929462652695bad04f0a27afb600974014a3c8b`. Parser library: Nager.PublicSuffix 3.8.0. CI ensures the file is bundled as `public_suffix_list.dat` next to `BoshaVault.exe`; do not remove it from the extracted folder.

**Upgrade:** replace the complete Windows archive after exiting the previous tray process, and reload extension version `0.5.0` from the new `browser-extension` directory. Confirm the native-host registration path if the folder moved. Use **fake** test accounts for `www.google.com` and `myaccount.google.com` to verify Exact/Related labels; do not submit those synthetic credentials to Google.
