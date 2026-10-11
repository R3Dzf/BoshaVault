# Lockvyn email confirmation and 2FA

Windows **1.10.0**, Android **1.6.0** (code 11). Account security is separate from the encrypted vault's master passphrase.

## Confirm your email

Create a cloud account with your email and a separate password of at least 16 characters. When the sender is configured, enter the emailed code in **Email confirmation code** and press **Confirm email code**. Then enter your cloud password and sign in. Confirmation does not save a cloud profile or skip 2FA. **Resend confirmation email** requests a new code; wait at least 60 seconds. Existing confirmation links continue to work if the owner's email template still uses links.

**Current deployment:** Brevo is the selected email provider. Its SMTP connection and the hosted confirmation template still need owner setup and a delivered-message check. Confirmation is still required; friends use their own Lockvyn accounts and do not need Supabase dashboard membership.

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

Use **Brevo** for transactional delivery. Never enter an SMTP password or private API key in source code or chat.

1. In Brevo, open **Settings → Senders, Domains, IPs → Senders → Add a sender**. Set the name to **Lockvyn** and use an email address you control. If prompted, enter the sender-verification code that Brevo sends to that address.
2. For your commercial sender, authenticate a domain you own using the DNS records Brevo provides. If you only have a free email address for an initial test, verify that sender address; Gmail/Yahoo domains cannot be authenticated by you. Brevo temporarily rewrites unauthenticated/free sender addresses, including transactional mail, and delivery must be checked. This is not a permanent branded sending address.
3. Open Brevo's **SMTP & API → SMTP** page and generate a dedicated **SMTP key** for Lockvyn. Copy the **SMTP login** displayed there. Depending on the account, this is an email address or a generated `…@smtp-brevo.com` address; do not assume it matches your account login. This page's SMTP key is different from a Brevo API key or your account password.

Open the dedicated project's [Email settings](https://supabase.com/dashboard/project/ghmbcfqiomggoxgwghjs/auth/providers), then SMTP configuration. Enter the credential directly into the Supabase dashboard:

| Setting | Value |
|---|---|
| Host | `smtp-relay.brevo.com` |
| Port | `587` (STARTTLS); `465` (SSL/TLS) is also supported |
| Username | The **SMTP login** shown on Brevo's SMTP page |
| Password | Your dedicated **Brevo SMTP key**, entered directly in the dashboard |
| Sender address | The exact sender email you verified in Brevo |
| Sender name | `Lockvyn` |

In [Email Templates](https://supabase.com/dashboard/project/ghmbcfqiomggoxgwghjs/auth/templates), choose **Confirm sign up**, set subject **تأكيد بريدك في Lockvyn**, and paste [confirmation.html](../supabase/templates/confirmation.html). Keep `{{ .Token }}` exactly as written: Supabase supplies the code. This template is prepared in source and is not yet applied to the hosted project. Codes do not depend on a website or browser redirect. Brevo is the SMTP transport; the confirmation template belongs in **Supabase**, not in Brevo's template editor.

Keep email confirmation enabled. Review email OTP expiry/length and Auth/provider rate limits; the clients accept 6–10 ASCII digits. Disable link tracking for authentication mail. Once configured, test signup, resend, code confirmation, password sign-in, 2FA and cross-user isolation using authorized recipients. If mail does not arrive, check **Brevo → Transactional → Email logs** and Supabase Auth logs without sharing codes or credentials. No real confirmation email has been sent in the automated checks.

## Backend enforcement and verification

`20261011150409_opt_in_mfa_enforcement.sql` is deployed. A restrictive RLS policy applies to SELECT/INSERT/UPDATE/DELETE and checks live verified factors through a private, caller-scoped helper. An AAL1 token is refused after MFA activation even if it was issued beforehand. AAL2 still must satisfy the original owner policies. Unverified setups and users who did not enable MFA retain optional cloud access.

Verification includes .NET/Java mocked Auth flows, live TOTP enrollment/login/refresh/removal and Data API checks with a disposable confirmed synthetic account, SQL RLS rollback tests, and Android emulator UI tests. Email delivery and hardware/device acceptance remain separate.

Primary references: [Supabase MFA](https://supabase.com/docs/guides/auth/auth-mfa), [TOTP](https://supabase.com/docs/guides/auth/auth-mfa/totp), [SMTP](https://supabase.com/docs/guides/auth/auth-smtp), [email templates](https://supabase.com/docs/guides/auth/auth-email-templates), [Brevo/Supabase SMTP](https://developers.brevo.com/docs/supabase-smtp-integration), [Brevo SMTP login and key](https://help.brevo.com/hc/en-us/articles/7924908994450-Send-transactional-emails-using-Brevo-SMTP), [sender verification](https://help.brevo.com/hc/en-us/articles/208836149-Create-a-new-sender-From-name-and-From-email), [free sender rewriting](https://help.brevo.com/hc/en-us/articles/14925263522578-Comply-with-Gmail-Yahoo-and-Microsoft-s-requirements-for-email-senders).
