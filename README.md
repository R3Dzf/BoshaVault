<div align="center">

<img src="design/lockvyn-mark.svg" width="104" height="104" alt="Lockvyn — shield and keyhole" />

# Lockvyn

**Your passwords. Your space.**

A privacy-focused, cross-platform password manager for **Windows** and **Android**.  
Keep your credentials organized, encrypted, and within reach — without making cloud access mandatory.

[![Build](https://github.com/R3Dzf/BoshaVault/actions/workflows/build.yml/badge.svg)](https://github.com/R3Dzf/BoshaVault/actions/workflows/build.yml)
![Windows](https://img.shields.io/badge/Windows-10%2F11%20x64-171B2E)
![Android](https://img.shields.io/badge/Android-9%2B-14866D)
[![MIT License](https://img.shields.io/badge/License-MIT-6558D9)](LICENSE)

**[Download for Windows](https://github.com/R3Dzf/BoshaVault/releases/tag/windows-v1.21.0)** &nbsp;·&nbsp;
**[Download for Android](https://github.com/R3Dzf/BoshaVault/releases/tag/android-v1.14.1)** &nbsp;·&nbsp;
**[Security](docs/SECURITY.md)**

</div>

---

## Made for everyday privacy

Lockvyn combines local-first storage with optional encrypted synchronization. Create and use your vault entirely offline, or connect your own devices to access your saved logins across Windows and Android.

The **Calm Pro** interface focuses on what matters: your vault, connected devices, encrypted backups, and settings — with advanced options available when you need them.

## What you can do

| Capability | Windows | Android |
| :--- | :---: | :---: |
| Encrypted vault, search, folders, favorites & notes | ✓ | ✓ |
| Password generator & password health checks | ✓ | ✓ |
| Encrypted export, restore & conflict-aware merge | ✓ | ✓ |
| Optional end-to-end encrypted cloud synchronization | ✓ | ✓ |
| Offline private Wi-Fi transfer | Host | Client |
| Autofill | Chrome / Edge extension (approval required) | Android Autofill |
| Quick access | System tray & keyboard shortcut | Biometric unlock |
| Smaller incremental app updates when compatible | ✓ | ✓ |
| Trusted domain groups for browser suggestions | ✓ | — |

<details>
<summary><strong>More about synchronization and device pairing</strong></summary>

- **Cloud sync is optional.** Account credentials and the vault's master passphrase are separate. The cloud service stores encrypted snapshots, not plaintext passwords.
- **Windows + Android:** A secure, time-limited, approval-based QR flow is available for connecting a phone to a Windows vault.
- **Android without a PC:** An Android phone can create and use its own vault and cloud account. Two phones can use the same encrypted cloud vault through the advanced recovery-code setup. A simpler phone-to-phone temporary QR flow is not yet available.
- **Offline transfer:** A private local Wi-Fi connection is supported between Windows and Android; encrypted file backups are another option.
- **Background sync:** Android limits background task timing. For the quickest synchronization, open and unlock the app on the receiving phone. Offline edits and conflicts are handled through the encrypted merge process.

**Treat cloud recovery codes like secrets:** they can provide access to your encrypted vault key. Never post them in issues, screenshots, or chat messages.

</details>

## Get started

### Windows

1. Open **[Download for Windows](https://github.com/R3Dzf/BoshaVault/releases/tag/windows-v1.21.0)** and download `Lockvyn-win-Setup.exe`.
2. Install and launch Lockvyn, then create a vault with a strong master passphrase.
3. Add your first login. Use **Sync & Devices** only if you want cloud sync or device pairing.
4. Future updates can be checked and applied inside the app. The updater shows the expected download size, including a smaller Delta package when available.
5. Under **Settings → Manage trusted domain groups**, add related hostnames such as `google.com` and `accounts.google.com` to suggest the same saved account across those subdomains. Every fill still requires approval.

> **Windows installer note:** Desktop release packages are checksum-verified, but do not yet carry an Authenticode publisher signature. Always download from this repository; do not disable antivirus protection to install.

### Android

1. Open **[Download for Android](https://github.com/R3Dzf/BoshaVault/releases/tag/android-v1.14.1)** and choose `Lockvyn-Android.apk`, or check for updates within your existing installation.
2. Install the signed APK **over the existing app** when updating; do not uninstall your vault.
3. Create or unlock your vault, then use **Vault**, **Sync**, **Backup**, or **Settings**.
4. Optional: enable Android Autofill and biometric unlock in Settings. The Autofill control updates its label from the real system setting and can disable Lockvyn as a provider.

Android may ask you to approve installation from this source. Keep Play Protect enabled and investigate any malware warning rather than blindly overriding it.

**Before upgrading an older experimental build, export and verify an encrypted backup.** Android only allows an in-place upgrade when the package identity and signing certificate match.

## How Lockvyn protects your data

```text
Master passphrase
       │
       ▼
Argon2id key derivation
       │
       ▼
Vault-key protection
       │
       ▼
AES-256-GCM encrypted vault
       │
       ├── Local device storage
       ├── Encrypted backup file
       └── Optional additional encryption → Cloud snapshot
```

- **Local-first:** Your vault works without creating an online account.
- **Authenticated encryption:** The vault uses Argon2id for passphrase-based key derivation and AES-256-GCM for encrypted data.
- **Optional encrypted cloud storage:** Cloud snapshots are protected with a separate key; access controls on the backend do not replace local encryption.
- **Protected access:** Auto-lock, device-specific protections and application-specific Autofill approval help reduce accidental exposure.
- **Recovery is your responsibility:** A lost master passphrase is not recoverable by Lockvyn support. Keep a verified encrypted backup and safeguard its passphrase.

> [!IMPORTANT]
> Lockvyn is an actively developed project and **has not undergone an independent security audit or penetration test**. Automated tests and strong cryptographic primitives are valuable but are not proof of complete security. Start with test credentials, review the [security model](docs/SECURITY.md), and report security concerns responsibly.

## Built with

| Layer | Technology |
| :--- | :--- |
| Windows desktop | C# · .NET · WPF |
| Android app | Java · Android SDK |
| Cryptography | Argon2id · AES-256-GCM · OS-protected key storage |
| Optional cloud | Supabase Auth · PostgreSQL RLS · encrypted snapshots |
| Windows updates | Velopack |
| Android updates | Verified APKs & binary Delta patches |
| Automation | GitHub Actions |

The Windows and Android implementations share a compatible encrypted vault format; platform integrations remain native.

## For developers

The source and tests are public. The repository uses GitHub Actions to build Windows and Android, exercise encrypted-vault interoperability, and run platform-specific checks.

```bash
git clone https://github.com/R3Dzf/BoshaVault.git
cd BoshaVault
```

The `main` branch tracks the latest Windows and Android source. Published releases remain pinned to their matching tags and release branches. For reproducible builds, check out the matching release ref and follow its [build guide](https://github.com/R3Dzf/BoshaVault/blob/release/windows-v1.21.0/docs/GITHUB-BUILD.md). Do not publish APKs with temporary signing keys as upgrades to the official Android app.

**Useful documentation**

- [Security model & limitations](docs/SECURITY.md)
- [Cloud account setup and 2FA](https://github.com/R3Dzf/BoshaVault/blob/release/android-v1.14.1/docs/CLOUD-ACCOUNT-SETUP.md)
- [Device pairing and its current limitations](https://github.com/R3Dzf/BoshaVault/blob/release/android-v1.14.1/docs/DEVICE-PAIRING.md)
- [Browser Autofill setup, Quiet Mode & profile support](https://github.com/R3Dzf/BoshaVault/blob/release/windows-v1.21.0/docs/BROWSER-AUTOFILL.md)
- [Windows updates](https://github.com/R3Dzf/BoshaVault/blob/release/windows-v1.21.0/docs/WINDOWS-UPDATES.md)
- [Android updates](https://github.com/R3Dzf/BoshaVault/blob/release/android-v1.14.1/docs/ANDROID-UPDATES.md)
- [Calm Pro design direction](https://github.com/R3Dzf/BoshaVault/blob/release/windows-v1.21.0/docs/CALM-PRO-UI.md)

## Project status

Windows and Android builds are available. The core vault, backups, optional encrypted cloud sync, basic device pairing and in-app updates are implemented. Browser Autofill, device-to-device flows, and some background behaviors still require broader hardware validation and usability work.

Issues and pull requests are welcome. Please avoid uploading real passwords, vault files, cloud recovery codes, authenticator secrets or signing keys.

## License

Licensed under the **[MIT License](LICENSE)**. Third-party packages retain their respective licenses — see [third-party notices](docs/THIRD-PARTY.md).

<div align="center"><sub>Lockvyn · Designed to make password management feel a little calmer.</sub></div>
