# Lockvyn cloud account security validation — 2026-10-11

Applies to Windows **1.10.0**, Android **1.6.0** (code 11). Synthetic credentials only; no real confirmation email sent.

| Verification | Result |
|---|---|
| .NET MFA and email-code transport | 21 checks passed: AAL/owner binding, pending/verified/unsupported factors, duplicate rejection, challenge binding, token rotation, local QR generation, large enrollment response, controlled errors and signup code |
| Android Auth transport on JVM | 22 checks passed with fake requests; no Android storage/network dependency |
| Live Supabase MFA/Auth/Data API | 18 checks passed: optional access, pending setup, wrong TOTP rejection, activation, AAL1 read/write denial, AAL2 access, password-only removal denial, refresh, fresh login challenge, removal/downgrade and sign-out |
| SQL restrictive MFA policy | Rollback suite passed: pending factor, AAL1 read/insert/update/delete denial, AAL2 owner access, cross-owner denial, missing claim denial |
| Supabase security advisors | No findings after private helper/policy deployment |
| Local Windows WPF / Android source compilation | Passed |
| Native Windows / Android builds and .NET–Java interoperability | Passed in [build #173](https://github.com/R3Dzf/BoshaVault/actions/runs/38152125598) |
| Expanded native Android email-code / MFA UI acceptance | Checks dismissal cleanup after Android dispatches its asynchronous OnDismiss callback; consult the latest PR build for the rerun result |

The temporary live account, factors, sessions and cloud snapshots were removed; remaining synthetic-user count was verified as zero. The owner selected Brevo; SMTP setup and the hosted code template remain pending. The code-confirmation UI and Lockvyn email template are prepared, but no delivered-message acceptance is claimed. Hardware UI, camera, biometrics and physical two-device acceptance remain separate.

---

# Lockvyn vault UX validation — 2026-10-11

Applies to Lockvyn Windows 1.9 / Android 1.5 source. The checks below use synthetic credentials and encrypted fixtures; no personal vault is modified.

| Verification | Result |
|---|---|
| .NET vault UX suite | 28 checks passed: folder defaults/persistence, configurable generator, review invalidation, permanent deletion, stale/concurrent merge refusal and schema 1 migration |
| Android engine on JVM | 18 checks passed, including .NET schema 2, Unicode folders, review fingerprints, permanent deletion and real schema 1 upgrade |
| Java → .NET UX compatibility | 3 checks passed: permanent deletion, review acknowledgement and Unicode/custom folder catalog |
| Existing .NET core suite | 74 checks passed |
| Cloud account/pairing/atomic merge | 17 checks passed, including confirmation resend and actionable restricted-sender errors |
| Windows WPF compile | Zero warnings/errors using .NET SDK 10.0.100 on Linux |
| Lockvyn identity and icon assets | Windows and native host compile; browser generator/origin/fill suites passed; XML/YAML, extension icon paths and all seven ICO sizes verified |
| Native Android keyboard and 1,000-row navigation | 12 checks passed on Android 35: recycled list, fixed Options, real visible keyboard, typed email/password, password masking and screenshot protection |

All four jobs passed in [build #171](https://github.com/R3Dzf/BoshaVault/actions/runs/38146133378) at `b14e2a6d25a455c6a23f666cf692081e826afad0`. The emulator used only synthetic entries and submitted no email/cloud request. Windows native DPAPI, TLS/Schannel, browser/native-host checks, both APK signatures and cross-language compatibility passed.

Downloaded ZIP/APK checks confirmed GitHub SHA-256 digests and archive integrity, actual Lockvyn product labels/versions, Android package `com.lockvyn.app`, compiled shield geometry, all seven Windows executable icon images, both x64 executable versions, extension assets and the bundled Arabic guide.

The isolated APK is **Lockvyn** (`com.lockvyn.app`) and has separate storage from production and the earlier Test app. Export an encrypted backup and update both devices before exchanging schema 2; older builds cannot open that schema. See [VAULT-UX.md](VAULT-UX.md) for the new controls and setup.

Public cloud signup still requires a configured SMTP sender. The connected Resend account currently has no sender domain. Native Windows UI, hardware Android behavior and real email confirmation remain acceptance work.

---

# Cloud preview validation — 2026-10-11

Applies to Windows 1.8 / Android 1.4 cloud-preview source and the dedicated Orchid Ledger project. Tests used synthetic entries only. No existing personal vault, unrelated Supabase project or original Android installation was modified.

| Verification | Result |
|---|---|
| Owner-isolation SQL transaction | Passed owner CRUD, cross-owner read/insert/update/delete and missing-identity/anonymous rejection; fixtures rolled back |
| Real Supabase Auth + Data API | 12 checks passed: encrypted roundtrip, two-owner isolation, anonymous/invalid JWT rejection, refresh rotation and local-session sign-out |
| Live-test cleanup | Zero temporary users and zero snapshot rows remained after cleanup |
| Supabase security advisors | No findings at validation time |
| .NET account, pairing, atomic merge | 15 checks passed, including bounded Auth response and PKCE confirmation payload |
| Java cloud encryption/pairing/atomic merge | 9 checks passed; .NET ciphertext opens in Java, tampering/identity mismatch refused, invalid later input preserves local file |
| Java → .NET outer encryption | Passed |
| Existing .NET core/transport suite | 74 core checks and cloud transport checks passed |
| Windows WPF compile on Linux | .NET SDK 10.0.100; zero warnings/errors; this is compilation, not native UI execution |

GitHub CI builds both platforms and runs native Windows DPAPI storage checks plus .NET/Java interoperability. Native Windows UI and Android Keystore/biometric behavior, real email confirmation, cancellation/backgrounding, offline reconnect and two-device edits still require acceptance testing. Export an encrypted backup first; use the isolated Android Test package alongside the existing app for preview testing.

Cloud acceptance: enroll one synthetic vault on both devices using its cloud code; sync A→B→A; make concurrent edits and inspect conflict copies; block networking and confirm offline edits; lock/background during each request and reopen; restart both apps and verify protected sign-in settings; try wrong account/code; change the master passphrase and follow the local reconciliation/fresh-space instructions in [CLOUD-SYNC.md](CLOUD-SYNC.md). No independent security audit is claimed.

---

> **Historical hotfix note (2026-10-09):** The following record describes the previous 1.2/1.1 builds. See `PAIRING-HOTFIX-2026-10-09.md` for native pairing acceptance steps.

# Validation record — 2026-10-09

Validation applies to the included 1.2 Windows / 1.1 Android source and preview artifacts. A successful build or unit test is not a claim that the native application has been run or that it is secure against all attackers.

## Completed

- .NET 10.0.401 / runtime 10.0.12: core, Windows WPF and tests compiled. Final Windows publish is self-contained, x64. Warnings are treated as errors.
- 36 C# behavioral/security checks passed: encrypted metadata, Unicode round trip, wrong-password rejection, modified headers/ciphertext, hostile/downgraded KDF settings, duplicate JSON, file/tag/version limits, fresh nonces, concurrent edits, repeated merge, convergence, deletion/restore, different vault refusal, failed import preservation, stale writer refusal, current-password verification, fresh-key rotation, old-key rejection, rotated-key adoption without loss, no key-epoch downgrade, exact HTTPS origins, random generator invariants and locked-session refusal.
- 29 Java/JVM behavioral/security and interoperability checks passed. These independently exercise the Java vault implementation against .NET-generated encrypted fixtures, strict JSON, tampered data, passphrase/key rotation, no key-epoch downgrade, conflict-ID compatibility, password generation and private-IP pairing validation.
- 17 additional QR checks passed using the actual Windows/core PNG renderer and Android luminance decoder: exact BV1 payloads on all three RFC1918 address ranges, 310px scaled display captured in synthetic 1280×720 frames, rotated frames, unrelated URL/public-IP rejection, truncated/oversized frames and blank images. These are decoder tests, not a live camera test.
- 9 additional desktop preference checks passed: missing/default settings, reopen persistence, atomic replacement/no leftover files, refusal of invalid timeout without overwriting, and corrupt/oversized/unsupported settings fallback.
- Desktop IPC integration checks are included under --desktop-tests but were NOT run successfully here: the environment denies the Unix socket backend used by .NET named pipes. Windows same-user restrictions, second-launch activation, stalled-client cancellation and foreground activation require native checks.
- 4 temporary TLS certificate checks passed: usable private key, actual signing/verification, different pin per session and bounded lifetime. These run on Linux and do not validate the Windows Schannel branch or a live handshake.
- The reverse Java → .NET encrypted-file check passed in a separate run. The test fixtures contain synthetic credentials and Arabic notes only.
- Android Java compiled against platform 35, and resource linking, D8 compilation, APK alignment and APK signature verification completed. Min SDK 28, target SDK 35. APK is non-debuggable, signed by one unique preview key using APK signature scheme v3. Version code is 2, version name 1.1-preview, signed with the same certificate as the original 1.0 preview (SHA-256: 8e3e4a09ecbfb0e35473f1ab055938324bc4ec3071a966e6f710ba0bcf279397). Its manifest includes INTERNET, USE_BIOMETRIC and optional user-granted CAMERA access. The QR scanning activity is unexported and camera hardware is optional.
- NuGet advisory scan of the core and its transitive dependencies returned no listed vulnerable packages at build time. That scan does not prove dependency safety or cover every Android library advisory.
- The UI design preview was rendered and visually inspected. It uses synthetic data and is explicitly a design preview, not a screenshot of the running Windows app.

## Not validated here

This build environment is Linux, with no Windows desktop, connected Android device, or hardware biometrics. Its network namespace has loopback only. Native UI behavior, real camera capture/focus/permission/lifecycle behavior, Android system Autofill/Keystore integration, Windows tray/close/restore/startup/logoff/Explorer-restart/second-instance behavior, Windows shortcuts/clipboard/display affinity, firewall behavior and live private Wi-Fi synchronization have NOT been exercised on the target devices. Attempts to start the LAN test host could not bind a private address in this environment; no successful end-to-end LAN test is claimed.

`tools/LanTest.java` and `--lan-host` in the C# test runner provide a repeatable native/network test for another environment. They are included so certificate pin mismatch, wrong token, a real encrypted GET/POST exchange and replay refusal can be checked on a private network. The LAN tests have not passed here. There has been no independent audit, penetration test, fuzz campaign or malware resistance certification.

## Required native acceptance checks

Use ONLY fake credentials while doing these checks.

1. Create a Windows vault, add a fake login with Arabic notes, edit it, close the app and reopen it. Try a wrong master passphrase. Confirm file contents do not contain fake plaintext credentials.
2. Use Ctrl+Alt+P while another window is focused. Copy a fake password, paste it into an intended test field, verify 20-second clearance and verify that a newer unrelated clipboard item is not cleared. Check Windows lock/suspend and inactivity lock.
3. Export an encrypted backup, import onto Android, and confirm the same fake data. Verify rotation/foreground transitions do not leak an unlocked screen into recents. Verify screenshots are refused where supported.
4. Enable Android Autofill through settings. Test the real installed browser on a test HTTPS page with a username/password field. Review and approve the actual package/certificate once. The `example.com` entry must never be suggested for `example.com.evil.test`, a different subdomain, HTTP or a missing-origin form.
5. Test a native app using a fake account. Explicitly link that login to that package/certificate. Confirm unrelated logins are not suggested and revoke approvals through Settings. Test changed certificate handling using a separate test app, not a production app.
6. Enable biometric unlock on a supported Android device, lock/reopen and authenticate. Cancel the biometric prompt. Change biometric enrollment and confirm the wrapped key becomes unusable while the master passphrase still works. Change the master passphrase and confirm biometric quick unlock is disabled.
7. Create a LAN transfer on Windows, enter its private Wi-Fi IP, and scan its QR inside Android. Confirm scanning fills the pairing code but does not connect until Connect & transfer is tapped; test camera denial, cancellation, backgrounding and manual paste. Confirm Windows hides its QR on expiry/completion and the displayed countdown works. Make changes on both devices, repeat transfer, and confirm convergence and conflict copies. Test wrong/expired codes, mismatched TLS pin and a consumed pairing code. Close/lock Windows and confirm the listener stops.
8. Test × while unlocked and while an unlock is running: app moves to tray and remains locked on return. Reopen by tray click, context menu, hotkey and a second EXE launch; confirm one process/window. Verify modal dialogs can be brought forward. Test normal minimization and restoration from maximized state. Restart Explorer and confirm the icon returns. Choose Exit and confirm no process/icon remains. Logoff/shutdown must proceed normally. Enable/disable Start with Windows and verify locked background startup on next sign-in. Reopen after changing the idle timeout and verify it persists.
9. Test a fresh LAN QR after the 1.2 Schannel certificate import change; confirm authentication on Windows. Keep an active session for more than two minutes but below three and confirm it is not prematurely closed by the default idle timeout. Expiry, explicit lock, tray close and exit must still stop it.
10. Rotate the key on one device and merge onto the other using the incoming passphrase. Confirm the new passphrase works there, old passphrase fails against the new file, and local unsynchronized edits remain. Retain and test an old encrypted backup separately.

Preserve encrypted backups before migration, reinstall or further development. Independent review of origin trust, master-key lifecycle, parsing, atomic write behavior and native integration is a prerequisite to representing this as a production security product.
