# Lockvyn Calm Pro — staged UI redesign

## Source of truth

Design-system colors for phase 1, shared on Windows WPF and Android native Java:

| Token | Hex | Meaning |
|---|---|---|
| Ink | `#171B2E` | Strong legible text |
| Muted | `#697185` | Secondary text |
| Brand | `#6558D9` | Primary action and focus |
| Canvas | `#F7F8FC` | Quiet surface |
| Soft brand | `#EFEDFF` | Selected/filter background |
| Divider | `#E1E5EF` | Subtle outlines |
| Success | `#14866D` | Verified successful state only |

## Phase 1 (this branch)

* **Android:** Remove all default button elevation, state-list animation and inherited shadow. Use ripple-only filled/outlined buttons with at least 50dp height, 56dp labeled fields and clear keyboard focus. Introduce vector navigation for Vault/Sync/Backup/Settings with touch targets, clickable labels, no fake elevated footer buttons. Refine vault filters, list row outlines, typography and unlock header. No changes to vault data, cloud logic, crypto, signing identity or package ID.
* **Windows:** WPF resource tokens, controls, focus states and flat buttons consistent with mobile. Reduce three dashboard banners to useful count and local security review; remove decorative shortcut panel, align main vault + details and simplify sidebar. Keep existing keyboard shortcuts and accessibility names. Version still reads the installed assembly.
* **Acceptance:** `python tools/test_calm_pro_ui.py`, signed Android compile, 1000-item native Android emulator navigation/keyboard/MFA smoke, Windows WPF build + real Velopack delta acceptance, cross-platform cloud encryption tests.

## Further phases — NOT implemented in phase 1

* **Phase 2:** Android login editor/details, cloud pairing, Backup and Settings as real reusable views with one back stack and persistent bottom navigation; replace transient stacked dialogs with full-screen pages/bottom sheets, preserve scroll position and draft validation. Accessibility audits and phone font size testing.
* **Phase 3:** Windows responsive navigation, unified modal design, refined update/download dialogs and master password flows, compact iconography, high-contrast UI and both platforms' light/dark theme switch.
* **Phase 4:** Device acceptance, real phone/PC pairing, QR and Wi-Fi fallback, upgrade and downgrade prevention, in-place update on Windows and Android, TalkBack/Narrator, snapshot/interaction tests; staged signed releases.

**Security:** UI refactors must never change ciphertext format, master password flows, recovery material handling, protected view flags, account RLS, Cloud auth tokens, installer identity or data paths. Test with non-sensitive sample logins. No release until compile and native smoke gates pass. No force merge into stable main.
