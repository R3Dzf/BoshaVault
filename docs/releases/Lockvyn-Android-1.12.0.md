# Lockvyn Android 1.12.0 — unified navigation and simpler secure sync

Four consistent areas: **Vault**, **Sync**, **Backup**, and **Settings**.

- **Vault:** everyday passwords and searching; favorites, review and trash stay available. The compact Tools menu contains only password generation and folders.
- **Sync & Devices:** shows cloud connection status, a direct entry into automatic or manual cloud sync, **Connect to my PC** for secure short-lived QR pairing, and collapses offline Wi-Fi transfer into an optional section.
- **Backup & Restore:** export or import/merge an encrypted vault, independent of cloud/Wi-Fi setup.
- **Cloud:** automatic sync and Sync Now stay visible; permanent recovery code, disconnect, forget local settings and authenticator account management stay behind **Security & advanced cloud options**.
- **Settings:** Autofill, fingerprint, app updates and cloud-security entry points are simple. Local diagnostics, revocation controls and master passphrase change are behind **Advanced security & troubleshooting**.
- The installed version appears on the lock screen and comes from Android PackageManager instead of hardcoded text.

**Android delta updates** remain supported from signed version 1.11.0. If the installed APK exactly matches a published baseline, the app downloads its much smaller pinned patch, reconstructs the new APK in temporary storage and verifies hash, signing certificate, package and version. Otherwise it downloads the complete APK. The Android system asks permission to install. Never uninstall to update.

The matching Windows release is **1.15.0**. Keep an encrypted backup and first test phone/laptop pairing with non-sensitive logins before treating hardware acceptance as complete.
