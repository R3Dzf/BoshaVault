# Trusted domain groups — Lockvyn Windows

The **Settings → Manage trusted domain groups** screen lets you group HTTPS hostnames, such as:

- `google.com`
- `www.google.com`
- `accounts.google.com`

Create a group called `Google`, enter one hostname on each line, and save. **Every stored login** matching any member of that group is suggested when visiting another member, without editing login entries. To change a group, select it and edit the hosts; deleting the group does not delete any logins.

The extension shows an account search box and puts exact, manually grouped, then other related subdomain suggestions in that order. The related suggestions are **not silent autofill**; each password fill requires a deliberate click and a Windows approval showing the current browser host and saved login host.

## Safety boundaries

- Hosts must be canonical lowercase DNS names, not URLs with paths or wildcards.
- The included offline Public Suffix List must confirm that every group member shares the same registrable domain. Unrelated websites, public-suffix tenants, and lookalike domains are refused.
- The user should visually verify the address bar before approving a fill. A malicious website can read a password after the user chooses to fill it.
- Group names and hostnames are local **desktop preferences** in `%LOCALAPPDATA%\BoshaVault\desktop.json`; they are not passwords and are **not encrypted**. Do not store sensitive notes in group names. They are currently not synced to Android.
- The encrypted vault format, saved credential URLs, browser approval requirement, and mobile vault are unchanged.
- Browser suggestions are limited to at most 50 matching entries per request to bound local message size. The search box filters those entries.
- Android Settings shows the current Autofill provider state after returning from the system settings screen; Android controls actual provider selection.

## Release requirements

Do not upgrade an Android installation with a CI APK signed by a temporary key. The final APK must use the same permanent signing certificate as the installed `com.lockvyn.app` package. Windows releases must use a higher version and pass the installed Velopack update and vault-preservation acceptance test.
