# Lockvyn Windows 1.16.0 — Calm Pro (phase 1)

This update introduces the visual foundation for the Calm Pro redesign. The current Windows app and companion Android retain their security, vault storage, encryption, cloud syncing, installer identity and existing shortcut behavior.

- Refined desktop **light** palette shared with Android: ink #171B2E, muted #697185, brand #6558D9, canvas #F7F8FC, subtle outlines.
- Cleaner WPF inputs, buttons, focus rings, consistent corners, compact list cards and keyboard states.
- Simplified sidebar labels and less visual clutter in the password dashboard: account count and local password review, without a redundant decorative privacy card.
- Existing **Sync & Devices**, **Backup & Restore**, **Settings**, Velopack delta size/progress UI and dynamically displayed installed version are preserved.
- No change to encryption keys, vault file format, Supabase migration, browser extension or backups.

Update in place from the Windows application. If the app cannot use an incremental Delta, its updater can download the full signed package. Do not uninstall the existing installation to update. Preserve a verified encrypted backup.

**Rollout scope:** This is **phase one**, not the complete Calm Pro redesign. A unified dialog and editor layout, new vector iconography on Windows, dedicated page navigation, Dark Mode, and the remaining accessibility polish are scheduled for later phases. Test on a real Windows PC before considering layout validated across monitors and DPI scales.
