# Lockvyn Windows 1.18.0 — Browser Autofill that survives updates

## New features
- **One-time browser connection** in Settings, with extension ID validation; no manual PowerShell needed for Chrome/Edge.
- **Quiet Mode** in Lockvyn Autofill extension v0.7.0: hide automatic suggestion panels on field focus and request logins only when needed.
- Local, read-only profile compatibility diagnostic for Chrome, Edge and other common Chromium browsers.

## Improvements
- **Automatic native host repair after Windows updates**: restores the path of an already user-approved Chrome/Edge native messaging host without repeated authorization, altering browser profiles, forcing browser policies or storing passwords in the extension.
- A single Windows user-level host registration covers all Chrome/Edge profiles that have chosen to install that extension. Optional repair-only mode in the setup script.
- Refreshed Calm Pro extension popup and inline suggestions.

## Fixes
- Reject malformed previously approved native extension origins during repairs.
- Preserve all prior allowed extension IDs across a new install location; refuse unauthorized wildcard access.

Install updates in place. The browser extension still requires explicit one-time installation per browser profile; Lockvyn does not silently install, hide, pin/unpin or force it. Unpin the toolbar icon manually if desired. Browser store auto-updates require publishing the extension to the corresponding official stores. Test with synthetic logins and verify the exact HTTPS website before approving fills.
