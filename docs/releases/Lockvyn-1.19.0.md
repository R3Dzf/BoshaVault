# Lockvyn Windows 1.19.0 — safer, less intrusive Browser Autofill

## New features
- **Compact Quick Fill approval** appears without restoring the entire Windows vault. It displays the exact current HTTPS hostname, the saved hostname, and the selected account — never the password.
- The confirmation closes after 30 seconds or Escape. The Windows vault must still be unlocked and a deliberate **Fill this login** click is required.

## Improvements
- The extension shows matching accounts first; less frequently used actions (other subdomains, save website, advanced password generation) are collapsed into **More options**.
- Users stay in their browser instead of being bounced into the full Lockvyn main window for every login.
- **Quiet Mode** remains available from the extension popup; no suggestions appear automatically when it is enabled.

## Security fixes
- Live top-frame/document identity now gets checked **before account metadata is listed**, not just before filling a password.
- Related-domain fills retain an explicit warning and approval; the browser checks that the page has not navigated while approval is pending.
- No unrestricted silent fill, credential caching in extension storage, or automatic website password access was introduced.

**Limitations:** An extension is part of the security attack surface. It can be compromised through its code or distribution, and a hostile website's JavaScript can inspect password fields after filling. The secure approval prompt reduces but cannot eliminate this risk. Restrict browser site access, review requested permissions, enable MFA on sensitive accounts, and avoid untrusted extensions. This project has not undergone independent security auditing.

The Android app, encrypted vault format, local vault data paths and Supabase cloud remain unchanged.
