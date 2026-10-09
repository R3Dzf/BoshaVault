# GitHub repository and build setup

This repository contains **the complete reviewed source snapshot** for BoshaVault Windows and Android, plus the local pairing hotfix. It does not contain production credentials, actual user vaults, APK signing keys, compiled binaries or private server data.

## Create the repository

**Simplest path on your own Windows PC:** Extract the ZIP, open PowerShell inside its `BoshaVault` folder and run `.\Publish-To-GitHub.ps1` after installing Git and GitHub CLI, then completing `gh auth login`. The script creates a new **private** repository and pushes the complete source. If PowerShell blocks local scripts, use `powershell -NoProfile -ExecutionPolicy Bypass -File .\Publish-To-GitHub.ps1` for this one run. Do not run unreviewed scripts.

Alternatively, do it manually:

1. On GitHub choose **New repository**, name it `BoshaVault`, mark it **Private** (recommended for a password manager under active security testing), and **do not** create a README or `.gitignore` online. Keep the default branch `main`.
2. Push every file under the `BoshaVault/` source directory to the **root** of the new repository (not an extra nested BoshaVault directory). The `.github/workflows/build.yml` file must exist at repository root.
3. Go to **Actions → Build BoshaVault (Windows and Android) → Run workflow** or push to `main`. When both jobs pass, get the ZIP and APK under **Run → Artifacts**. Workflow artifacts expire after 14 days.
4. This CI checks builds and selected tests; **it cannot validate real phone↔PC Wi-Fi connectivity**. See `docs/PAIRING-HOTFIX-2026-10-09.md` and `Diagnose-BoshaVault.ps1`.

## Important Android signing warning

On a normal push or manual run **without signing secrets**, CI generates a **one-run-only test signing key** and signs the APK with it so that it can be installed *only for test usage*. It cannot upgrade the previously distributed BoshaVault Android APK or other CI builds. The temporary key is destroyed after the run. **Do not uninstall an existing vault** unless you already tested restoring its encrypted backup and know your passphrase. A Git tag `v*` build **fails** rather than accidentally shipping this temporary-key APK.

To get a repeatably signed APK that upgrades an existing installation, the certificate and alias must match the APK you already have. The previous preview signing key is **not in the source archive or GitHub**, so simply adding a new signing key will **not** allow seamless updates of the old APK. Export an encrypted backup before any planned reinstall.

### Optional stable Android signing via repository secrets

Create a permanent PKCS#12 key *locally on a trusted computer*. Keep the `.p12` backup offline; do not email it, attach it to an issue or commit it. In **Settings → Secrets and variables → Actions**, configure:

- `BOSHAVAULT_ANDROID_KEYSTORE_BASE64` — base64 of the `.p12` file bytes, with no spaces/newlines.
- `BOSHAVAULT_ANDROID_KEYSTORE_PASSWORD` — password for both keystore and key.
- `BOSHAVAULT_ANDROID_KEY_ALIAS` — alias stored in that file.

PowerShell can produce the base64 text with:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('C:\private\BoshaVault-Signing.p12'))
```

Never share these secret values with ChatGPT or put them in a conversation. You can optionally avoid exposing long-term production signing material to cloud CI by building and signing on your own trusted machine instead.

## Windows

The workflow uses `windows-latest`, .NET 10 SDK, and builds the native WPF app as a `win-x64` self-contained executable. It exercises vault and crypto tests, desktop tests, temporary certificate checks, and a *loopback* Schannel/TLS handshake. A successful loopback test **does not** prove Windows firewall rules, Wi-Fi isolation, or camera pairing on real devices.

The Windows executable is **not Authenticode-signed**. Don't disable antivirus for this project. The application's tray-close behavior and start-with-Windows functionality are retained from Windows version 1.2.

## Android

CI uses Java 17, Python 3.11, Android SDK platform 35 and build tools 35.0.0. The lightweight Python builder downloads Maven libraries with hard-coded SHA-256 checks. Version code is `3` (`1.2.0-pairing-hotfix`) for the updated Android code.

## What is *not* verified

The project has not undergone an independent cryptographic/security audit; there is no guarantee against malware on the host. No CI service can test your home's network or whether Android/Windows agree on the exact QR and TLS connection on your devices. Test first with disposable, fake credentials.
