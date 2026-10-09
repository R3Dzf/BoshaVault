# Android biometric compatibility test (v1.3.1)

Reported symptom on TECNO Spark 30 Pro: fingerprint dialog succeeds, then AndroidKeyStore AES-GCM Cipher.doFinal throws IllegalBlockSizeException. This points to a cryptographic authorization/provider failure after authentication, not a failed finger scan. The exact low-level KeyMint error on that device remains unknown.

## Change
New enrollments use Android Keystore AES-CBC-PKCS7 with the **per-operation BIOMETRIC_STRONG** requirement and a BiometricPrompt CryptoObject. This CBC operation encrypts **only a random 32-byte vault key**, never passwords or user records. The wrapped key is not treated as valid until the vault's AES-GCM authentication succeeds. Old GCM quick-unlock wrappers can still be opened; enrollment resets the quick-unlock wrapper only, not the vault.

This follows the Android documentation's biometric CryptoObject examples and does **not** silently allow unauthenticated keys or weak biometrics. Some OEM Keystore implementations can fail for CBC too; do not claim fixed until tested on a real device.

## Safe live test with existing vault preserved
The CI artifact `BoshaVault-Android-BIOMETRIC-TEST` is an **independently installable app**:
- Application ID `com.bosha.vault.test`, label `BoshaVault Test`; its Android-private file directory differs from the production app.
- Install it **alongside** `com.bosha.vault`. Do **not** uninstall, clear data, or overwrite the original app.
- Create a disposable vault using a fake master passphrase and fake account data **only**.
- Enable fingerprint, then lock and unlock with fingerprint three times.
- Cancel a biometric prompt; verify that the master passphrase continues to work.
- Restart the app and retry; optionally test changing fingerprints on a test-only device.
- Never import the production vault into the test app.

Each CI run signs the test APK with a temporary key, so **do not use test builds for real data**. They cannot update each other reliably.

## If it still fails
Use the reported exception *including the nested cause* to identify any Keystore/KeyMint error code. Ask for **redacted** Android logcat from a narrow time interval if further investigation is required. Do not upload unfiltered logs, vault files, pairing codes, or signing keys.

## Production signing
A build success is not a release-validation success. See `docs/RELEASE.md`. To upgrade an existing installation without losing data, the APK package and signing identity must match. A newly created signing certificate cannot update an APK signed by a different certificate. Do not uninstall the original before an encrypted backup is verified and the old signer situation is resolved.
