# Lockvyn Android 1.14.1 — Accurate Autofill switch

## Improvements
- Settings reads the real Android Autofill provider state and labels the button **Enable Android Autofill** or **Disable Android Autofill** accordingly.
- Disable now calls the Android API to turn off the app's **own enabled Autofill service**; it does not reopen the enable prompt.
- On returning from Android settings, the label and provider status refresh to reflect the actual state.
- Status messages clarify when Android did not accept the change.

## Safety
- Disabling Autofill doesn't remove saved passwords, encrypted vault data, fingerprint keys, or trusted device pairing.
- Site credential matching and save/fill approval policies remain unchanged.
- **Do not install a temporary-key CI APK over an existing Lockvyn installation.** The official Android release must be signed with the same permanent certificate as the installed `com.lockvyn.app` package. Never uninstall a real password vault to resolve a signing mismatch.

## Release process
Publish only after Android emulation and interop tests pass, the permanent signing secrets are configured, and release signature continuity checks succeed.
