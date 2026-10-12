# Lockvyn builds, installation and Android signing

The existing [R3Dzf/BoshaVault repository](https://github.com/R3Dzf/BoshaVault) now builds the Lockvyn product. Source project names, encrypted file identifiers and compatible storage paths retain their original names. The repository contains source and synthetic tests, not user vaults or private signing keys.

## Build and download

Open **Actions → Build Lockvyn (Windows and Android)**. Pushes to main, pull requests targeting main and manual workflow runs trigger the build. The Windows, Android, cloud interoperability and Android native UI jobs must pass. Artifacts expire after 14 days.

| Artifact | Contents and installation |
|---|---|
| `Lockvyn-Windows-x64` | `Lockvyn-Windows-x64.zip`; extract its complete contents, then run `Lockvyn.exe`. Includes `Lockvyn.NativeHost.exe`, browser extension, registration script and Arabic `START-HERE.html`. |
| `Lockvyn-Android` | `Lockvyn-Android.apk` and signing notice. New package `com.lockvyn.app`, label **Lockvyn**, version **1.8.0**, code **13**. Installs beside production and earlier Test/Preview packages. |
| `Lockvyn-Android-LegacyPackage` | Compatibility APK using the original `com.bosha.vault` package and signing notice. Only the matching original certificate permits an in-place update. |
| `Lockvyn-Android-UI-Logs` | Emulator and instrumentation logs for native keyboard/list acceptance. |

Windows is self-contained x64 with .NET 10, version **1.12.0**. It has no publisher Authenticode signature. The Android application supports Android 9+, targets SDK 35 and uses pinned build dependencies. Product labels and version strings have no Preview suffix.

Export an encrypted backup before changing versions. Exit the old Windows app from its tray menu before replacing program files. Windows continues to find the vault under `%LOCALAPPDATA%\BoshaVault`; do not delete that directory. See [browser Autofill setup](WINDOWS-AUTOFILL.md).

Install the separate **Lockvyn-Android** package while keeping the old app installed. Use **Import encrypted vault** on the lock screen or **Receive from your Windows app** to move the same vault. Check its entries before relying on the new installation. Update both devices before exchanging schema 2; these versions open old schema 1 files, while old versions cannot read schema 2. See [VAULT-UX.md](VAULT-UX.md).

## Android signing

A normal push/PR run generates a temporary certificate, signs and verifies both APKs, then destroys the private key. These installable APKs cannot upgrade an installation signed by another certificate, including a previous CI run. The separate Lockvyn package avoids overwriting the old app. An existing Lockvyn installation still requires an encrypted export before any planned reinstall with a different certificate.

Tag `v*` builds and manual **signed_release** runs on main require permanent signing secrets and fail if they are absent. The matching original signing certificate and alias are required to upgrade the legacy installed APK. Creating a new key does not solve that mismatch. Current signed-release mode produces the legacy-package compatibility artifact.

### Configure stable signing

Create and protect a permanent PKCS#12 key on your own trusted computer. Keep its backup offline. Configure these repository Actions secrets:

- `BOSHAVAULT_ANDROID_KEYSTORE_BASE64`: base64 of the `.p12` bytes.
- `BOSHAVAULT_ANDROID_KEYSTORE_PASSWORD`: password for keystore and key.
- `BOSHAVAULT_ANDROID_KEY_ALIAS`: key alias.

The original secret names remain supported. Do not share their values in a conversation or commit them. `tools/Configure-AndroidSigning.ps1` can configure the existing workflow locally; alternatively build and sign locally without handing a permanent private key to CI.

## What CI verifies

Windows runs core/crypto, UX, desktop, DPAPI, temporary certificate and loopback Schannel/TLS tests, publishes both executables, checks the browser extension and tests native-host registration/protocol. Cross-language tests verify encryption, schema migration, folders, review acknowledgement and permanent deletion using synthetic fixtures.

The native Android job builds an isolated app plus matching-signed instrumentation, starts a fresh Android 35 emulator, creates 1,000 synthetic logins, verifies row recycling and fixed Options, opens the real cloud dialog, taps email/password fields and verifies the visible keyboard and typed input. It checks screenshot protection and password masking. It never sends a signup or real email request.

CI does not validate your real phone/PC Wi-Fi, camera, biometric hardware, Chrome/Edge integration or two-device cloud behavior. No independent security audit is claimed. Check [VALIDATION.md](VALIDATION.md) for actual outcomes and [CLOUD-SYNC.md](CLOUD-SYNC.md) for public signup's remaining SMTP setup.

The canonical permanent-signing release uses **Release Lockvyn Android** and `android/version.json`. See [Android updates](ANDROID-UPDATES.md) for in-app updates, encrypted-backup migration, version increments and the `release/android-v<versionName>` publication flow.
