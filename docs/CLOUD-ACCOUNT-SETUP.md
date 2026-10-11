# Lockvyn email confirmation and 2FA

Windows **1.11.0**, Android **1.6.0** (code 11). Account security is separate from the encrypted vault's master passphrase.

## Confirm your email

Create a cloud account with your email and a separate password of at least 16 characters. Enter the emailed code in **Email confirmation code** and press **Confirm email code**. Then enter your cloud password and sign in. Confirmation does not save a cloud profile or skip 2FA. **Resend confirmation email** requests a new code; wait at least 60 seconds. Existing confirmation links continue to work if the owner's email template still uses links.

Cloud accounts require email confirmation; friends use their own Lockvyn accounts and do not need Supabase dashboard membership. The project owner configures transactional SMTP and tests inbox delivery separately.

## Enable or manage 2FA

1. Connect your cloud account, then open **Cloud account security · 2FA** on the cloud screen.
2. Choose **Enable 2FA**. Scan the QR using a TOTP authenticator or enter the setup key manually with account name your email, time based, SHA-1, six digits, 30 seconds. QR generation is local.
3. Enter a current authenticator code and choose **Verify code & enable**. An unverified setup does not enable 2FA.
4. Android locks when you leave it. If you switch to your authenticator, return, unlock Lockvyn, reopen cloud security and choose **Finish setup**. Enter the code for the key you already added. **Enable/Add another authenticator** begins a new setup and removes abandoned, unverified TOTP factors.
5. Back up your authenticator using its encrypted backup feature or add another authenticator. Sign-in offers a choice between verified TOTP factors. The cloud connection code is an encryption key, not an MFA recovery code.
6. **Remove [authenticator]** asks for a current code from a verified factor. Removing the last verified factor turns off 2FA; removing one of several leaves the others enabled.

Future password sign-ins require a code for accounts that enabled 2FA. A remembered session that already passed 2FA keeps its AAL2 assurance across refresh and does not require a code for every manual sync. If its assurance falls to AAL1, sync requests a code. Codes, setup keys and QR images are not saved in the cloud profile, logs or shipped artifacts. Closing/locking clears UI references; managed strings cannot be guaranteed erased from memory.

Losing every authenticator locks access to the cloud account. This release does not include MFA recovery codes or self-service MFA reset. The offline vault and encrypted local backups remain usable with their master passphrase. Lost cloud passwords also have no recovery UI in this release.

## Account email messages

The source includes separate Arabic templates for confirmation, account recovery, email changes, reauthentication, email login, invitations and seven security notification types. See [the template catalog](../supabase/templates/README.md). Templates alone do not add a native recovery, invitation or email-change screen. Real inbox delivery is checked separately by the account owner.
