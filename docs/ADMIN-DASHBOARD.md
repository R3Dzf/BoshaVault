# Lockvyn administration

The account-support dashboard runs as the `lockvyn-admin` Supabase Edge Function.

Open: `https://lockvyn-admin-ui.r3dzf-studio.workers.dev/` (Cloudflare HTML host). The Supabase Edge Function is the authenticated JSON backend, not a browser-facing HTML renderer.

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
   `https://lockvyn-admin-ui.r3dzf-studio.workers.dev/recover`
   Verify the recovery email template uses the canonical `{{ .ConfirmationURL }}` reset link. Only your Supabase email provider should send recovery emails.
6. Test on a **non-production account**: signing in without permission must fail, signing in without AAL2 must fail, recovery should send to the **account owner**, and a second request within one hour must be refused.

The allowlist is empty by default. Do **not** use a public email comparison, a hardcoded admin password, or a client-side admin flag.

## Important limitation
The vault master passphrase is not the cloud account password. Admins cannot reset or reveal it. The only honest recovery options are an unlocked trusted device or a user-held encrypted backup and its decryption credentials. Supabase service operators can see account metadata and ciphertext, not the plaintext vault.

## Security Center
- The admin dashboard shows a bounded 90-day feed of first-party cloud-session, support, recovery and verified-user admin access-denial events.
- Event details are enumerated metadata with actor/subject UUIDs and server timestamps; password/OTP failures, emails, IP addresses, user agents, tokens and vault data are not copied into the feed.
- The feed requires the same confirmed-user, AAL2, live-session and allowlist checks as other admin routes. The private SQL table is not directly exposed to client roles.
- No customer-wide MFA enforcement or Supabase Auth provider rate-limit setting is changed by this feature. See [Phase 04 security-center rollout notes](ADMIN-PHASE-04.md) for the retention and rollback boundary.

## Release management
Phase 06 adds a private audited rollout policy for Windows and Android. Native clients read only the sanitized platform policy and keep update eligibility local. The public release-policy route does not require an admin session; policy writes do.

## Announcements and notices
Phase 07 adds bilingual English/Arabic drafts, preview, publish/archive, platform/version targeting, expiry, and optional in-app alerts. Drafts and audit rows stay in the private schema. Admin writes require the verified AAL2 active administrator session and fresh TOTP. The public feed returns only current published notices that match the requested platform, version, and locale; clients send no account or device identifiers.

Windows and Android store the alert preference locally, disabled by default. They fetch only when the user opens the notices view or opts into in-app alerts. Offline errors do not affect vault access or sync, and local notice IDs suppress repeat prompts without being uploaded. This feature uses no remote push provider or operating-system notification permission.

## Browser HTML hosting
Supabase Edge Functions serve HTML as text on their default project domain. The login page is served through a dedicated Cloudflare Worker (`infra/cloudflare/lockvyn-admin-ui.js`) that renders HTML with a strict nonce-based CSP. The browser signs in directly with Supabase Auth and sends only authorized admin API requests to the Edge Function from the explicitly allowed Cloudflare origin. No administrative service-role secret is shipped to the Worker or browser.
