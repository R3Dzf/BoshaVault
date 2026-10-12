# Android updates and permanent signing

The stable Android identity is `com.lockvyn.app`. Its Java namespaces and encrypted vault format stay compatible with older backups. `android/version.json` controls `versionName`, monotonically increasing `versionCode`, package identity and minimum SDK. The canonical release is always `Lockvyn-Android.apk`. The separate legacy package is retained for compatibility/testing; it is not the stable update channel.

## Using updates

From the locked screen or Settings & Autofill, choose **Check for app updates**. Check, download, then explicitly install. Starting updates waits for a vault operation to finish, locks the vault and clears the clipboard. Network requests are cancelled when the update screen leaves the foreground. Android asks the owner to allow installs from Lockvyn when needed, and confirms installation of each APK.

The app reads published, stable `android-v*` releases from this GitHub repository. It validates the public `android-update.json`, package, strictly newer version code, Android requirement, size, SHA-256 and equality of the APK signing certificate with its installed certificate. Android's installer verifies the APK cryptographic signature before replacing the app. No repository token is shipped to the app. The content provider is private and grants read access to one cached update APK; it cannot expose vault files or accept writes.

Updates download the whole APK. Temporary CI APKs use a discarded test certificate and cannot upgrade to a different signing identity. Moving to stable signing is a one-time migration: export and verify an encrypted backup before removing a temporary canonical app. An older `com.bosha.vault` app can stay installed alongside the canonical app during encrypted-backup import. Subsequent stable updates keep the same package and signing key and do not uninstall the app.

## Publishing

1. Configure the permanent PKCS#12 signing key through the repository's existing three Actions secrets: `BOSHAVAULT_ANDROID_KEYSTORE_BASE64`, `BOSHAVAULT_ANDROID_KEYSTORE_PASSWORD`, `BOSHAVAULT_ANDROID_KEY_ALIAS`. Keep an owner-controlled offline backup. Never commit or publish the private key or password.
2. Increase `android/version.json` and add customer release notes under `docs/releases/Lockvyn-Android-<versionName>.md`. Upload the source and wait for **Build Lockvyn** to pass on that exact commit.
3. Create `release/android-v<versionName>` at the tested commit. **Release Lockvyn Android** builds with the permanent key, checks certificate lifetime, package/version and signing continuity, then publishes the APK, metadata and SHA-256 sums. Missing signing configuration fails; it never falls back to a temporary certificate. Tags `android-v*` work too. Manual dispatch creates a draft unless `publish` is selected.

The signing secrets are available only to production signing jobs, never the pull request or emulator job. The signing file is restricted and removed after signing. Release metadata refuses a signing-identity change or non-increasing version code against existing published Android releases.

CI performs real package-manager upgrade acceptance with synthetic versions `9.0.0` to `9.0.1` on the disposable emulator: same-signer APK acceptance, different-signer and damaged APK rejection, read-only provider boundaries, denied install permission, in-place upgrade, encrypted vault hash and reopening, device identity and app preferences. Synthetic APKs and test keys are never published as stable releases.
