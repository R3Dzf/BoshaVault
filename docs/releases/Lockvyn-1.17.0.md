# Lockvyn Windows 1.17.0 — Better scrolling, dialogs & update notifications

## New features
- Optional release alerts: check for updates in the background, show a tray notification when hidden, and offer **Update now**, **Later**, or **Don't notify me again** on opening Lockvyn.
- **What's New** opens a short summary of new features, improvements and fixes from the official GitHub release notes. Manual update checks always remain available.
- A Settings checkbox re-enables optional release notifications after opting out.

## Improvements
- Slim rounded scrollbars and bounded pixel scrolling avoid the exaggerated two-finger trackpad jumps in lists of many logins.
- The folder dropdown is labeled and styled as a compact filter beside search, not a large standalone button.
- Modern Calm Pro app dialogs have a clean title bar, Close control, rounded layout, keyboard focus and move/resize capability.
- The existing update screen still displays official Delta and Full sizes plus progress before installation.

## Fixes
- Popup alerts do not interrupt sensitive open dialogs, and never require access to an unlocked vault.
- Public release notes are size-limited and untrusted text, not executable content.

Update in place through Check for updates, preserving the vault. Test with sample data first. The app still requires real Windows trackpad and DPI acceptance; automated WPF and installer tests are not a substitute for physical hardware verification.
