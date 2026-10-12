# Lockvyn Windows 1.21.0 — Smarter Autofill and calm navigation

## New
- **Trusted domain groups:** Settings → Manage trusted domain groups lets you explicitly group HTTPS hostnames such as `google.com`, `www.google.com`, and `accounts.google.com`. The group applies to all saved logins without rewriting entries. Add, edit, and delete groups at any time.
- **Search saved accounts:** Type an account name or email directly in the browser Autofill suggestions to filter up to 50 candidates.
- **Safer suggestion order:** Exact site first, trusted group second, and other related same-site hosts after that.
- **Smoother Windows folders:** Updated All folders dropdown with rounded items and a clearer selection affordance.
- **Cleaner browser overlay:** Thin rounded scrollbar, smaller panel and less distraction from password-generation actions.

## Security and compatibility
- Trusted groups cannot bridge unrelated registrable domains or independent private public-suffix tenants. The local offline Public Suffix List is required for matching.
- Related/grouped account fills still show the saved host and the current browser host in the explicit Windows approval dialog. No silent-fill authority is granted by grouping.
- Vault encryption and saved account URLs are unchanged. Group definitions are local desktop preferences and are **not synced** to Android. Group names and hostnames are stored unencrypted in local preferences; do not put private notes in group names.
- No changed Master Passphrase, vault format or cloud credential migration.

## Updating safely
1. Keep a verified encrypted vault backup. Do not uninstall the current app or delete its data to upgrade.
2. Update using Lockvyn's built-in Windows updater after the **published** Windows 1.21.0 release appears. The updater checks package integrity and may fall back to the complete package when necessary.
3. Unlock Windows Lockvyn and reload the installed Chrome/Edge extension page after updating. Each browser profile may require an extension reload.
4. In Settings, create a trusted `Google` group with the desired hostnames, then focus the password field on `accounts.google.com`. Search for your selected email and explicitly approve filling the password.

The downloadable build artifacts from a pull request are **test builds**, not a published signed release. Wait for the official release after all checks pass.
