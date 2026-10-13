# Lockvyn administration

The account-support dashboard runs as the `lockvyn-admin` Supabase Edge Function.

Open: `https://ghmbcfqiomggoxgwghjs.supabase.co/functions/v1/lockvyn-admin`

## Scope and security
- View a paginated list of Supabase Auth accounts: email, registration, last sign-in, confirmed-email status and verified 2FA status.
- Request a recovery email **for the user's separate cloud login password**. The user sets their own new password. Recovery requests are logged and rate-limited to one per hour per target account.
- Administrator sign-in requires a confirmed email, an explicit server-side allowlist entry and verified Authenticator 2FA (AAL2).
- No operator endpoint for creating/reading vault entries, decrypting stored snapshots, replacing the vault master passphrase, obtaining cloud recovery keys or modifying encryption keys.
- The `service_role` key lives only inside Supabase Edge Function secrets; never expose it to a browser, repository or public client.
- Dashboard pages do not save access tokens to local/session storage. Account listing is scoped to the currently approved operator.
- Edge Function allows unauthenticated GET of the login page, but **all admin data/actions** require an independently verified Supabase JWT + AAL2 + membership check. No user-provided headers or user metadata can grant admin authority.

## First-time setup
1. Apply `supabase/migrations/20261013123000_admin_console.sql` to the Lockvyn Supabase project.
2. Have the owner sign up or log in with a **verified** Lockvyn cloud account and enable verified TOTP 2FA using the Lockvyn app.
3. Confirm the account ID/email through the Supabase Auth Users console. **Only after confirming the correct identity**, run the allowlist statement on the dashboard project:
   ```sql
   insert into public.lockvyn_admins(user_id)
   select id from auth.users
   where email = 'OWNER_VERIFIED_EMAIL' and email_confirmed_at is not null
   on conflict (user_id) do nothing;
   ```
4. Deploy `supabase/functions/lockvyn-admin` with `verify_jwt=false`. The login page is public, but each POST request verifies the signed user token, AAL2, and the allowlist independently. Configure the function through the Supabase project backend only.
5. In Supabase Auth → URL Configuration → Redirect URLs, add exactly:
   `https://ghmbcfqiomggoxgwghjs.supabase.co/functions/v1/lockvyn-admin/recover`
   Verify the recovery email template uses the canonical `{{ .ConfirmationURL }}` reset link. Only your Supabase email provider should send recovery emails.
6. Test on a **non-production account**: signing in without permission must fail, signing in without AAL2 must fail, recovery should send to the **account owner**, and a second request within one hour must be refused.

The allowlist is empty by default. Do **not** use a public email comparison, a hardcoded admin password, or a client-side admin flag.

## Important limitation
The vault master passphrase is not the cloud account password. Admins cannot reset or reveal it. The only honest recovery options are an unlocked trusted device or a user-held encrypted backup and its decryption credentials. Supabase service operators can see account metadata and ciphertext, not the plaintext vault.
