# Lockvyn Android 1.13.0 — Calm Pro (phase 1)

Focus: fix unwanted raised/shadowed buttons and make daily password management clearer without changing encryption, cloud sync, local files, autofill credentials, update identity or permissions.

- **No native button shadows:** Removes the default Button state-list elevation animator, elevation and translationZ; uses bounded ripple interaction instead.
- **Consistent primary/secondary buttons:** Filled Calm Pro purple for important actions, white outlined secondary actions with even 50dp or larger touch targets.
- **Real bottom navigation:** Four destinations, Vault, Sync, Backup and Settings, with tinted vector icons and accessible labels instead of the previous row of four raised buttons.
- **Cleaner Vault:** Bordered login rows, separated scrolling items, accessible All/Favorites/Review/Trash filter chips, refined header and primary Add Login action.
- Updated unlock screen spacing; 56dp outlined fields preserve keyboard focus and system font sizing.
- Existing encrypted cloud pairing, original recovery fallback, offline Wi-Fi, Master Password, biometric unlock, Autofill and Delta updates remain as they were.

**Update normally from within Lockvyn** or the signed GitHub APK, without uninstalling. The update may use a smaller Delta only if your installed APK matches an official baseline; otherwise it downloads the whole APK. Android still requires installation confirmation.

**Next design phases (not included yet):** Replace stacked dialogs with full-screen mobile pages and bottom sheets, consolidated detail/editor screens, light/dark theme toggles, high-contrast accessibility passes and physical-phone checks. The Android build uses aapt2+ECJ, so icon lookup respects its resource-linking mechanism rather than relying on generated R.java.
