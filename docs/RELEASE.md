# BoshaVault signed release procedure

## Current status
A successful GitHub Actions build means the binaries compiled and automated tests passed. It is **not** a device validation, an independent security audit, or proof that Wi-Fi transfer and biometric unlock work on the TECNO device.

### Android — permanent APK signing identity
The APK on each normal push is a **temporary-test-key** build, not an updateable release. Do not distribute it as the final product.

1. On a trusted Windows device install Java JDK and GitHub CLI, then run `gh auth login`.
2. From repository root run `powershell -NoProfile -File .\tools\Configure-AndroidSigning.ps1`.
3. Enter the keystore password twice when prompted by keytool and once when prompted by the script. Do not enter it in chat. The script generates a **persistent local .p12**, then uploads the necessary three values privately as encrypted GitHub Actions repository secrets.
4. Back up the `.p12` and its password **offline**; without them, future updates cannot use the same Android identity.
5. Trigger a signed build: `gh workflow run build.yml -R R3Dzf/BoshaVault --ref main -f signed_release=true` or go to **Actions → Build BoshaVault → Run workflow → signed_release: true**.
6. After both jobs pass, open the workflow run's **Artifacts → BoshaVault-Android-APK**. Its signing notice should say it used the project-provided signing certificate. Run `apksigner verify --verbose --print-certs` on the APK and keep the SHA-256 certificate fingerprint for subsequent releases.
7. Confirm the fingerprint is unchanged before each update.

**Existing installations:** Android permits APK updates without data loss only when the app package and signing identity are compatible. A new release keystore **cannot** replace an installed preview with a different certificate. The original old signing private key cannot be extracted from an APK. If unavailable, first export an encrypted backup, verify restoration using a test copy, and keep your master passphrase. Do not uninstall or clear data before this.

### Windows — separate publisher signing
The Windows EXE produced by GitHub Actions is currently **unsigned**. An Android .p12 is not an Authenticode publisher certificate. A Windows build signed with a self-created certificate is *not* automatically trusted by Windows or SmartScreen. For a publisher-verified Windows executable, supply a valid code-signing identity from a trusted provider and integrate a protected signing step. Do not disable Microsoft Defender to install an unsigned preview.

### Conditions before calling this production-ready
- Test fingerprint enrollment, enable, unlock, cancel and enrollment change on real phones.
- Test QR, transfer, interruption and retry on a real Windows-PC ↔ Android Wi-Fi connection.
- Test backup export/import and upgrade preservation.
- Verify signed releases and certificate identity persistence across two consecutive builds.
- Complete an independent security audit, especially of key handling, Autofill, and transfer protocol.

For reproducibility do not commit keystores, passphrases, local vaults, or signing secrets to this repository.
