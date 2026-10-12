# Lockvyn Windows 1.15.0 — streamlined Sync, Backup & Updates

This release brings a clearer and smaller navigation structure to Windows, without removing recovery mechanisms.

- **My Vault** keeps logins, favorites, review, trash and password generation (rarely-used filters appear in an expandable section).
- **Sync & Devices** is the place to view the cloud connection, sync, add a phone with the secure two-minute QR and use optional local Wi-Fi transfer.
- **Backup & Restore** has only Export encrypted backup and Import / merge encrypted backup on the main screen.
- **Settings** keeps security, preferences and software updates. Firewall settings, private IP selection, legacy pairing codes and cloud account recovery are under explicit Advanced options.
- **Software update** shows current and available versions, expected Delta/Full package sizes (derived directly from the Velopack feed), percentage and download/verification progress, before asking to restart. The updater can fall back to a full package if its delta cannot be reconstructed.
- The Windows version shown on the lock screen and sidebar is now read from the **running assembly**, never a hardcoded release label.
- Preserves the Windows 1.14.1 fix that allows `BVPAIR2:` short-lived device-pairing QR codes.

Install using **Check for updates → Download update → Restart and install**. Vaults and profile data remain stored in the existing local folder. No uninstall or reinstall is required. Keep a verified encrypted backup when testing any significant change.

**Progress disclosure:** Velopack's callback reports combined transfer and reconstruction/checksum progress, not verified raw network bytes. The UI displays a real percentage and package download sizes, and never fabricates an exact received byte count or speed. Package sizes are estimates of required transfer; the fallback may download the full package.

The Android companion for this release is **1.12.0**. Both apps still need real-device acceptance for first-time pairing and some OS-dependent background-sync behavior.
