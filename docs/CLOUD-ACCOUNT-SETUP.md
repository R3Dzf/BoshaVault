# Lockvyn email confirmation and 2FA

Windows **1.10.0**, Android **1.6.0** (code 11). Account security is separate from the encrypted vault's master passphrase.

## Confirm your email

Create a cloud account with your email and a separate password of at least 16 characters. When the sender is configured, enter the emailed code in **Email confirmation code** and press **Confirm email code**. Then enter your cloud password and sign in. Confirmation does not save a cloud profile or skip 2FA. **Resend confirmation email** requests a new code; wait at least 60 seconds. Existing confirmation links continue to work if the owner's email template still uses links.

**Current deployment:** general email sending remains unconfigured. The owner has no sender domain or SMTP service ready. Resend has no verified domains. Confirmation is still required; friends use their own Lockvyn accounts and do not need Supabase dashboard membership.

## Enable or manage 2FA

1. Connect your cloud account, then open **Cloud account security · 2FA** on the cloud screen.
2. Choose **Enable 2FA**. Scan the QR using a TOTP authenticator or enter the setup key manually with account name your email, time based, SHA-1, six digits, 30 seconds. QR generation is local.
3. Enter a current authenticator code and choose **Verify code & enable**. An unverified setup does not enable 2FA.
4. Android locks when you leave it. If you switch to your authenticator, return, unlock Lockvyn, reopen cloud security and choose **Finish setup**. Enter the code for the key you already added. **Enable/Add another authenticator** begins a new setup and removes abandoned, unverified TOTP factors.
5. Back up your authenticator using its encrypted backup feature or add another authenticator. Sign-in offers a choice between verified TOTP factors. The cloud connection code is an encryption key, not an MFA recovery code.
6. **Remove [authenticator]** asks for a current code from a verified factor. Removing the last verified factor turns off 2FA; removing one of several leaves the others enabled.

Future password sign-ins require a code for accounts that enabled 2FA. A remembered session that already passed 2FA keeps its AAL2 assurance across refresh and does not require a code for every manual sync. If its assurance falls to AAL1, sync requests a code. Codes, setup keys and QR images are not saved in the cloud profile, logs or shipped artifacts. Closing/locking clears UI references; managed strings cannot be guaranteed erased from memory.

Losing every authenticator locks access to the cloud account. This release does not include MFA recovery codes or self-service MFA reset. The offline vault and encrypted local backups remain usable with their master passphrase. Lost cloud passwords also have no recovery UI in this release.

## Owner: finish email delivery

Use an owned domain with Resend, or an existing SMTP provider. Never enter an SMTP password or private API key in source code or chat.

For Resend, verify the domain's DNS records and sending status in [Domains](https://resend.com/domains). The domain must belong to you; the testing sender does not deliver unrestricted mail to friends. Create a sending key in the provider dashboard and enter it directly into Supabase's server-side SMTP settings.

Open the dedicated project's [Email settings](https://supabase.com/dashboard/project/ghmbcfqiomggoxgwghjs/auth/providers), then SMTP configuration. For Resend:

| Setting | Value |
|---|---|
| Host | `smtp.resend.com` |
| Port | `465` (TLS) |
| Username | `resend` |
| Password | Your Resend sending API key, entered directly in the dashboard |
| Sender address | An address on your verified sending domain |
| Sender name | `Lockvyn` |

In [Email Templates](https://supabase.com/dashboard/project/ghmbcfqiomggoxgwghjs/auth/templates), choose **Confirm sign up**, set subject **تأكيد بريدك في Lockvyn**, and paste [confirmation.html](../supabase/templates/confirmation.html). Its `{{ .Token }}` placeholder is supplied by Supabase; it sends a code instead of a redirect link. This template is prepared in source and is not yet applied to the hosted project. Codes do not depend on a website or browser redirect.

Keep email confirmation enabled. Review email OTP expiry/length and Auth/provider rate limits; the clients accept 6–10 ASCII digits. Disable link tracking for authentication mail. Once configured, test signup, resend, code confirmation, password sign-in, 2FA and cross-user isolation using authorized recipients. No real confirmation email has been sent in the automated checks.

## Backend enforcement and verification

`20261011150409_opt_in_mfa_enforcement.sql` is deployed. A restrictive RLS policy applies to SELECT/INSERT/UPDATE/DELETE and checks live verified factors through a private, caller-scoped helper. An AAL1 token is refused after MFA activation even if it was issued beforehand. AAL2 still must satisfy the original owner policies. Unverified setups and users who did not enable MFA retain optional cloud access.

Verification includes .NET/Java mocked Auth flows, live TOTP enrollment/login/refresh/removal and Data API checks with a disposable confirmed synthetic account, SQL RLS rollback tests, and Android emulator UI tests. Email delivery and hardware/device acceptance remain separate.

Primary references: [Supabase MFA](https://supabase.com/docs/guides/auth/auth-mfa), [TOTP](https://supabase.com/docs/guides/auth/auth-mfa/totp), [SMTP](https://supabase.com/docs/guides/auth/auth-smtp), [email templates](https://supabase.com/docs/guides/auth/auth-email-templates), [Resend SMTP](https://resend.com/docs/send-with-smtp).
