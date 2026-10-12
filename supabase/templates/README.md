# Lockvyn account email templates

Thirteen separate Arabic, right-to-left templates cover Supabase Auth's documented email types. Each has a distinct subject and case-specific instructions. Deployment and SMTP transport are configured separately from these source files.

| Case | Source file |
| --- | --- |
| Confirm signup | confirmation.html |
| Reset cloud account password | recovery.html |
| Confirm email change | email-change.html |
| Reauthenticate | reauthentication.html |
| Email login code | magic-link.html |
| Invite user | invite.html |
| Password changed | password-changed.html |
| Email changed | email-changed.html |
| Authenticator added | mfa-added.html |
| Authenticator removed | mfa-removed.html |
| Sign-in identity linked | identity-linked.html |
| Sign-in identity removed | identity-unlinked.html |
| Phone changed | phone-changed.html |

`catalog.json` maps the documented template types to source files and subjects. Generate with `python tools/generate-email-templates.py`; validate Go template expansion with `go run tools/validate-email-templates.go` using synthetic values. Edit the generator rather than generated files.

Authentication emails use `{{ .Token }}` for native code entry. The clients currently implement signup confirmation and password login; these other templates do not introduce new native recovery, invitation or account-edit screens.

The email templates contain no vault contents, password, authenticator secret, tracking image or user metadata. TOTP codes come from the user's authenticator. Email account recovery does not recover a forgotten local vault passphrase. Rendering checks do not prove real inbox delivery.

Reference: [Supabase's public email-template documentation](https://supabase.com/docs/guides/auth/auth-email-templates).
