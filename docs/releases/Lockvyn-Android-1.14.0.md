# Lockvyn Android 1.14.0 — Authenticator return & cleaner scrolling

## New features
- Short bounded 90-second 2FA return: when switching specifically from an open authenticator-code/setup dialog to another app, Lockvyn keeps that step in memory. Return with the current code without re-entering the vault passphrase if the phone stays unlocked and Android keeps the process alive.

## Improvements
- The login list uses an 3dp overlay scrollbar and dialog scrollers are less intrusive.
- Pressing a login for longer uses a rounded clipped ripple rather than the old rectangular ListView selector.

## Fixes
- Normal app backgrounding still locks your encrypted vault. Closing/locking the phone, expiring the 90-second 2FA grace period, or Android destroying the process locks the vault and clears the dialog.
- No recovery codes or authentication codes are stored to survive process death; cloud and vault files are unchanged.

Update in place from Lockvyn 1.13.0. The Android Delta updater will prefer the pinned signed difference if your installed APK matches an official baseline; it falls back to the complete APK safely. Keep an encrypted backup and verify this on your TECNO phone because OEM background process policies vary.
