> **GitHub CI:** The complete Windows + Android source can be built with [GitHub Actions](.github/workflows/build.yml). See [GitHub build, installation, and Android signing](docs/GITHUB-BUILD.md). Run [Publish-To-GitHub.ps1](Publish-To-GitHub.ps1) locally with GitHub CLI to create a private repo and upload all files. Unconfigured CI produces an APK with a temporary *test-only* signing certificate; it cannot upgrade the previous installed APK. The pairing hotfix has not been verified on actual connected devices.

> **Pairing hotfix source update (2026-10-09):** See [`docs/PAIRING-HOTFIX-2026-10-09.md`](docs/PAIRING-HOTFIX-2026-10-09.md). This is source-only and has not passed live Windows/Android pairing. `Build-Windows-Hotfix.ps1` builds the updated desktop after running tests; `Diagnose-BoshaVault.ps1` provides read-only diagnostics. Existing signed Android APK is unchanged.

# BoshaVault Windows 1.6 browser Autofill + login editor fixes / Android 1.3.x

Native password vault for Windows x64 and Android 9+. English UI, Unicode credentials and notes. A local Chrome/Edge browser extension preview is included for user-approved filling; there is no cloud account, advertising, analytics or external synchronization service.

New groundwork: a local Password Health report, standalone RFC 6238 TOTP engine and independently encrypted optional Supabase snapshot format are in source. Windows 1.5 preview also includes **local Chrome/Bitwarden CSV import with human review**, browser document-ID origin verification, lookalike hostname warnings and user-confirmed updates to existing saved passwords. **Cloud Sync, saved TOTP, Windows Hello, passkeys, CSV import and automatic signed updates are not enabled in the shipped UI yet.** See [the cloud security design](docs/CLOUD-SYNC.md) and [roadmap/status table](docs/ROADMAP-SECURITY.md).\n\n**Windows 1.6:** fixed a closed-Shadow-DOM click bug that caused login suggestion presses to disappear without filling. Added username-first login support, live Chrome/Edge toolbar actions, optional www/bare-host match, one-click capture of the current HTTPS website/username into the new-login form, and a compact WPF dialog with Generate + Copy password buttons. Browser extension is still a hardware-untested preview. See [Windows Autofill](docs/WINDOWS-AUTOFILL.md).

This is a working implementation and a preview build, not an independently audited security product. There is no promise of invulnerability or impossible decryption. See `docs/SECURITY.md` and `docs/VALIDATION.md` before using real credentials. Use synthetic credentials for initial device testing.

## Included

| Feature | Windows | Android |
|---|---|---|
| Create, unlock and lock encrypted vault | Yes | Yes |
| Add, edit, search, favorites, folders, notes | Yes | Yes |
| Local checks for short and reused passwords | Yes | Yes |
| Secure random password generator | Yes | Yes |
| Trash and restore, with synchronized tombstones | Yes | Yes |
| Encrypted file backups and conflict-preserving merge | Yes | Yes |
| Private Wi-Fi transfer with TLS certificate pin and temporary token | Host | Client |
| Temporary pairing QR | Display + countdown | Offline camera scanner |
| Master passphrase change with fresh vault key | Yes | Yes |
| App inactivity lock | Default 2 min | Default 2 min; also on leaving app |
| Clear owned clipboard after 20 seconds | Best effort | Best effort |
| Quick access | Tray icon / Ctrl + Alt + P / relaunch | Launcher / native Autofill |
| Close window | Locks vault and moves to tray; Exit is explicit | Locks when leaving app |
| Optional start at sign-in | Locked, hidden in tray | Not included |
| Biometric quick unlock | Not included | Android Keystore + biometric CryptoObject |
| Autofill | Chrome/Edge extension preview; exact HTTPS host + Windows confirmation; manual copy fallback | System Autofill; authenticated selection |

The Windows extension reads the browser-reported HTTPS origin and offers only exact-host matches after the vault is unlocked. When creating a new account, its popup can generate a cryptographically random password, fill a clearly detected confirmation field, and save the generated password with the signup username/email after explicit review in Windows. Each credential release still requires a confirmation on Windows; it never types into arbitrary desktop applications. See [Windows browser Autofill setup](docs/WINDOWS-AUTOFILL.md). Ctrl+Alt+P brings the app forward. Copy and paste is the fallback accepted in the design discussion. Android rejects insecure or missing browser origins, ambiguous forms and unverified WebViews. A browser's signing identity must be approved on first use and match thereafter. Native-app filling requires explicitly linking each login to that package and certificate.

No passkeys, TOTP manager, continuous/background synchronization, automatic password saving, Windows Hello unlocking, cloud sync, breach lookup or recovery service is included. LAN sync is an explicit transfer, not a promise of automatic discovery. Features absent from the UI are not hidden placeholders.

## Run

Windows: extract the full `BoshaVault-Windows-x64` artifact and start `BoshaVault.exe` without administrator privileges. Keep the native host, browser-extension folder and installer script together for Chrome/Edge autofill setup. See [installation guide](docs/WINDOWS-AUTOFILL.md). It bundles .NET 10.0.12; no separate runtime installation is required. Data is under `%LOCALAPPDATA%\BoshaVault`. The binary has no publisher Authenticode signature. Do not disable antivirus to run it.

Android: the **previously distributed** `BoshaVault-Android.apk` is version 1.1, signed with the same certificate as the original 1.0 preview. The **updated Android source** now builds version 1.2.0 pairing-hotfix (version code 3). A GitHub CI APK signed with an ephemeral test key cannot update that existing version, even with a higher version code. Do not uninstall until you have a restorable encrypted backup. Export an encrypted backup before updating as a precaution. It is signed with a unique preview-build key, is not debuggable, and has no shared debug signing key. A subsequent APK signed with a different key cannot update this install: export an encrypted backup before uninstalling. Build and sign your own releases with a persistent private signing key; that private key is not part of this source package.

Windows tray: closing the main window with × locks the vault, clears the owned clipboard, stops a transfer and hides the window; the app stays available in the notification area. Click the B tray icon (possibly under the hidden-icons arrow), choose Open BoshaVault, press Ctrl+Alt+P, or launch the executable again to reopen the existing instance. Minimize keeps the taskbar window; normal inactivity locking still applies. Right-click the tray icon → Exit BoshaVault fully closes it. Logoff/shutdown exits normally; session lock/suspend locks the vault. A one-time notification explains the behavior.

Right-click the tray icon → Start with Windows, or use the same setting in Your vault settings. This is off until you enable it, uses your user account only, and starts locked in the tray without opening an unlocked window. Use a permanent executable folder; toggle the option off/on if you move the executable. If you previously added a shortcut under shell:startup, remove that old shortcut after enabling the built-in setting to avoid a duplicate launch. Auto-lock choices persist in a small non-secret desktop.json beside the vault.

For a Windows update, export an encrypted backup, exit the old version completely (the original 1.1 exits with ×; 1.2 exits through the tray), then replace the executable in its existing folder. Running the published EXE requires no administrator privileges. The Android APK remains 1.1 and does not need reinstalling for this desktop update.

Choose a unique master passphrase of 16+ characters. A long passphrase with randomly chosen words is useful. Nothing is prefilled with real credentials. Forgetting it without an available biometric unlock means the vault cannot be reset. Biometric access is tied to this Android device and is not a backup.

## Share the same vault

Create the vault on ONE device, then transfer/import it onto the other. Creating independent vaults on both devices creates separate identities which cannot be merged.

1. Connect both devices to the same private Wi-Fi network and unlock the Windows vault.
2. Windows → Devices & backup → verify the laptop's private Wi-Fi IPv4 address → Start a private transfer. A QR and three-minute countdown appear.
3. Android → Devices & encrypted backup → Connect to Windows over Wi-Fi → Scan QR code. On a new phone without a vault, use Receive from your Windows app on the welcome screen instead.
4. Allow camera access when asked and scan the QR shown by Windows. The code is filled in automatically; tap Connect & transfer. Scanning alone does not initiate a network connection.
5. Enter the vault's master passphrase locally if asked, then tap Unlock & complete transfer. Keep the Windows transfer window open until it reports completion. No passphrase is sent to another device.
6. You can paste the `BV1:` code instead of scanning. It expires after 3 minutes and allows one download plus one merged upload. The QR disappears on completion or expiry. Show it only to your own phone.

Both devices must be on the same private network. A guest network may isolate clients. If Windows presents a firewall prompt, restrict the permission to the private network. The listener binds the selected local IP only and closes after completion, expiration, app locking or closing the transfer window. An explicitly active transfer may keep the vault open for its bounded three-minute window; it cannot keep it unlocked indefinitely. Closing to tray or locking Windows always stops it. Windows displays connection/expiry progress; restarting generates a new port and QR. No passphrase or plaintext credential crosses the link. Certificates are short-lived and explicitly pinned by the pairing code; Windows imports the temporary TLS key into a user key container for Schannel compatibility, without PersistKeySet, and releases it on disposal. The container carries the temporary TLS key only. No root certificate is trusted or installed. On other platforms the key remains an ephemeral import; ordinary HTTP and public-IP endpoints are refused.

The in-app QR scanner runs offline, accepts only valid BoshaVault private-IP pairing codes, and neither saves camera images nor opens web URLs. The camera is released when the scanner closes or the app leaves the foreground. If permission is denied or the camera is unavailable, manual paste remains usable.

You can instead export, transfer and merge encrypted backups by USB or a file manager. File transfers remain encrypted. Concurrent edits create `(conflict)` copies; review those copies before changing them. App trust approvals are device-local and are not granted by merging a remote vault.

Changing the master passphrase creates a fresh random vault key and advances the authenticated key epoch. On another device, merging this update requires its incoming passphrase. It verifies and adopts a newer key while retaining local edits. Merging an old backup cannot downgrade the current key. If both devices independently rotate their key to the same epoch, automatic reconciliation is refused; retain both backups and recover them separately.

## Build and verify

Windows / core require .NET 10 SDK:

```powershell
dotnet run --project src/BoshaVault.Tests -c Release
dotnet run --project src/BoshaVault.Tests -c Release -- --desktop-tests
dotnet run --project src/BoshaVault.Tests -c Release -- --tls-certificate-tests
dotnet publish src/BoshaVault.Windows -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o dist/windows
```

Android requires Java 17+, Python 3, Android platform 35 and build tools 35. Generate and protect your own signing key with `keytool`, then:

```powershell
$env:BOSHAVAULT_KEYSTORE_PASSWORD = 'your signing password'
python tools/build-android.py --sdk 'C:\Android\Sdk\platforms\android-35' --tools 'C:\Android\Sdk\build-tools\35.0.0' --keystore 'C:\private\release.p12' --alias boshavault
```

The Android source uses native Java controls and platform integrations. Sharing the encrypted file format instead of a MAUI dependency lets both the platform UI and its security APIs stay native. Cross-language tests verify that the two implementations agree. The C# core is not used as a dependency in the Android APK.

Android crypto uses Bouncy Castle 1.86 only for Argon2id, and Android's JCE for AES-GCM. The build pins downloaded dependencies by SHA-256. Java JVM interoperability tests additionally use JSON-java (test only). `tools/InteropTest.java`, `tools/LanTest.java` and `tools/QrInteropTest.java` use fake credentials only. QR rendering uses QRCoder 1.8.0; Android QR decoding uses ZXing core 3.5.4. Never include your production vault or private signing keys in source control.

QR interoperability checks (Java 17 JDK with `javac`; or compile the same files with the pinned ECJ build compiler):

```bash
dotnet run --project src/BoshaVault.Tests -c Release -- --qr-fixtures build-deps/qr-fixtures
javac -cp build-deps/bcprov.jar:build-deps/json.jar:build-deps/zxing-core.jar -d build-deps/java-core android/app/src/main/java/com/bosha/vault/VaultEngine.java android/app/src/main/java/com/bosha/vault/StrictJson.java android/app/src/main/java/com/bosha/vault/LocalTransfer.java android/app/src/main/java/com/bosha/vault/PairingQrDecoder.java tools/QrInteropTest.java
java -Djava.awt.headless=true -cp build-deps/java-core:build-deps/bcprov.jar:build-deps/json.jar:build-deps/zxing-core.jar com.bosha.vault.QrInteropTest build-deps/qr-fixtures
```

Use semicolons instead of colons for Java classpath separators on Windows. The QR fixtures are synthetic and do not start a server. These checks cannot replace camera and Wi-Fi testing on real devices.

## License

Your project source is provided under MIT (`LICENSE`). Dependencies retain their own licenses; see `docs/THIRD-PARTY.md`.
