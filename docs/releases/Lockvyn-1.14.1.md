# Lockvyn Windows 1.14.1 — secure Add device QR fix

This is a focused Windows-only hotfix for first-time secure pairing with Android 1.10.0 or later.

**Fixed:** Clicking **Sync → Add device · secure QR approval** in Windows 1.14.0 incorrectly displayed **“A Lockvyn pairing code is required.”** The QR renderer only recognized legacy Wi-Fi and cloud codes and rejected the new short-lived `BVPAIR2:` invitation. The renderer now accepts and validates temporary pairing invitations before showing the QR. The new QR holds only an ephemeral public key, session ID and expiry, never a permanent cloud recovery key.

**Try pairing:** Update Windows to 1.14.1, unlock the vault, choose **Sync → Add device · secure QR approval**, then on Android choose **Connect to my PC · scan secure QR**. Sign in to the same Cloud account, compare all six verification digits on both screens and approve on Windows only if they match. Keep a verified encrypted backup before testing with real credentials.

Use **Check for updates → Download update → Restart and update**. Windows update bundles and the delta from 1.14.0 are published through GitHub Actions. Existing local vault data and cloud configuration are not reset.

This hotfix has automated QR-rendering and cloud tests, but device pairing still needs acceptance on a real Windows PC and Android phone.
