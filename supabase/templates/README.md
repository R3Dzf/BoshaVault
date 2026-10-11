# Lockvyn account emails

Thirteen separate Arabic, right-to-left templates cover every email case exposed by this project's Supabase Auth dashboard. Brevo sends them through the saved custom SMTP connection; the template renderer and event triggers are Supabase Auth.

| Case | File | Current use |
| --- | --- | --- |
| Confirm signup | confirmation.html | Native Windows/Android six-digit email confirmation |
| Reset cloud account password | recovery.html | Prepared; a recovery screen is not yet available in the native clients |
| Confirm a new email address | email-change.html | Prepared for an explicitly requested email change |
| Reauthenticate | reauthentication.html | Prepared for an account action requesting a fresh email code |
| Email login code | magic-link.html | Prepared; native clients currently use email/password login |
| Invite user | invite.html | Prepared; no public invitation flow is enabled |
| Password changed | password-changed.html | Security notification enabled |
| Email changed | email-changed.html | Security notification enabled |
| Authenticator added | mfa-added.html | Security notification enabled |
| Authenticator removed | mfa-removed.html | Security notification enabled |
| Sign-in identity linked | identity-linked.html | Prepared; notification disabled while linked identity providers are unused |
| Sign-in identity removed | identity-unlinked.html | Prepared; notification disabled while linked identity providers are unused |
| Phone changed | phone-changed.html | Prepared; notification disabled while phone authentication is unused |

`catalog.json` maps dashboard routes to subjects, files and notification preferences. Generate with `python tools/generate-email-templates.py`; validate actual Go template expansion with `go run tools/validate-email-templates.go` (synthetic values only). Edit the generator rather than individual generated files.

Authentication codes use `{{ .Token }}` because the current native apps do not handle browser confirmation redirects. No QR, authenticator secret, vault contents, password, tracking image or user metadata enters the templates. The six email-code cases concern the cloud account. TOTP 2FA codes come from the user's authenticator, and a forgotten local vault passphrase cannot be recovered by email.

Hosted template changes are saved in Supabase separately from source deployment. On 2026-10-11 all 13 bodies were saved and checked again after reloading the dashboard, and the four listed security alerts were confirmed enabled. A saved SMTP configuration and a rendered template do not prove inbox delivery; test with the account owner's explicitly chosen test address and review Brevo transactional logs. Using a verified sender on an owned, authenticated domain is the later branded-sender step.
