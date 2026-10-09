# Android Autofill — activation, appearance, privacy and troubleshooting

## What the user should see

1. In BoshaVault open your encrypted vault with the master passphrase, then open **Settings & Autofill**.
2. Read **Autofill provider status**. If not selected, tap **Enable Android Autofill**, choose BoshaVault, and confirm. Android allows only one selected autofill service per user/profile.
3. Create a saved login containing a real HTTPS URL (for browsers), username and password. For native applications the user must approve a one-time per-login link to the installed app.
4. Switch to a supported login page such as an HTTPS website in Chrome and tap a login field. Android may show a **Fill with BoshaVault** suggestion near the focused field (dropdown). The exact position is controlled by Android and the keyboard.
5. Tap the suggestion. The unexported BoshaVault unlock screen appears; unlock with the master passphrase (or working fingerprint). Approve the browser's package signing certificate the first time. Pick a login whose exact HTTPS hostname matches the browser origin.
6. Android fills only the identified username/password fields. Nothing is sent to a website by BoshaVault itself; credentials become visible to the selected browser/app when Android fills them.

No accessibility overlay, clipboard watcher, automatic typing, or hidden page injection is used. Browser matches require Android to report a verifiable HTTPS origin. Unverified WebViews, HTTP, ambiguous password fields and mismatched origins are **intentionally rejected**. Username-only login steps are supported when the site explicitly identifies a username input.

**This build provides Android's menu/dropdown presentation.** Suggestions directly embedded in the keyboard (inline IME) are **not yet implemented**; a keyboard that only shows inline chips may display nothing. An AndroidX inline UI implementation and an on-device keyboard compatibility matrix are future work. Do not enable `supportsInlineSuggestions` in the service XML until actual inline presentations are created.

## If nothing appears

1. Return to **Settings & Autofill → Refresh Autofill status**.
2. If it says **Not selected**: choose BoshaVault as the active Android autofill provider. On Tecno/HiOS or other OEMs, settings names vary; use the in-app enable button rather than guessing a menu path.
3. If **Last request: never**, the Android system did not call BoshaVault's AutofillService. Tap the password field, try Chrome and the default keyboard, verify the app allows Android Autofill, and check Autofill is not disabled for the user/profile.
4. If **No recognized login field**, the site/app has not exposed hints recognized by the current Autofill service. Try another trusted login page and report the site/app *name only*, not your credentials.
5. If **destination could not be verified**, Android did not supply an acceptable HTTPS origin or a trusted signing identity. Refusal is a security feature; do not bypass it.
6. If **Android was given a BoshaVault suggestion** but nothing was visible, the AutofillService returned a response. The problem may be the keyboard or the target app's UI. Try tapping the field again, restarting the app, or another browser. The keyboard-inline UI is not in this release.
7. If Autofill selected **BoshaVault Test** but your real vault is in **BoshaVault**, you are using different app storage; change the active autofill service back to your real app. Never move production vaults into test apps.

Status diagnostics store **only a bounded status code and timestamp** in private local preferences. They do not store package names, visited websites, input text or passwords. The "Clear Autofill diagnostics" button removes those values.

## Testing and release acceptance criteria

- Selected / not-selected state updates after returning from Android Settings.
- Chrome login form with supported password and username hints gets a dropdown suggestion.
- A verified HTTPS origin matches only the saved account for its exact hostname.
- Native app requires explicit linking and revalidates its signing certificate on use.
- Username-only step can fill username but never a password into an unrelated field.
- App with two or more password fields, HTTP or unverified WebView never receives credentials.
- Returning with a canceled unlock never fills data.
- Test with different IMEs: Gboard and the TECNO system keyboard, plus Chrome and at least one native app.
- Tests must run on physical Android devices; GitHub Actions compilation alone does **not** establish Autofill UI visibility.

Do not uninstall or clear data in the original app. A CI APK signed by an ephemeral key cannot upgrade an existing APK that uses another signing certificate.
