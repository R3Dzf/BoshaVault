# Phase 02 — Account support actions (in progress)

**Hard budget: $0.** Uses the existing Supabase Free instance and free CI/local GoTrue;
no paid branches/projects/SMTP or commercial monitoring. No vault decryption.

## Implemented support workflow

1. The operator signs in with cloud password + TOTP (AAL2), as before.
2. Select another **non-admin** user's cloud account. Owner/admin accounts
   cannot be targeted or suspended from this dashboard.
3. Choose a **fixed reason category**, not free text (reduces risk of storing
   secrets), and choose a single action.
4. **Prepare** generates a server-side 5-minute, single-use support-case ID,
   without modifying the target.
5. **Confirm** requires entering the target's exact full email plus a **fresh
   TOTP challenge** from the operator's own authenticator. The server verifies
   the authenticated JWT's signed AMR claim and requires TOTP within 180s.
   A fresh JWT issuance without new TOTP does not satisfy this requirement.
6. Atomic server-only PostgreSQL claim prevents replay, rechecks target
   email, actor authorization, target!=actor, and target is not an admin.
7. If the kind is `suspend_24h`, GoTrue's official
   `auth.admin.updateUserById(target, {ban_duration:'24h'})` is used;
   `restore_login` uses `{ban_duration:'none'}`.
8. Status is committed to a service-role-only audit table. If the provider
   responds with error, mark failed. If completion fails after Auth mutation,
   **do not retry** automatically; manually reconcile the case, because
   the Auth action may have already succeeded.
9. `session_review`, `mfa_review`, `deletion_review` record
   **review requests only**. They DO NOT sign anyone out, delete data, disable
   MFA or authorize takeover. Proper review/identity-proofing workflows and
   retention limits remain to be implemented before closing Phase 02.

## Scope and limitations

- Cloud login suspension blocks new logins/token refreshes as the provider
  enforces its ban. **Existing issued JWTs remain valid until their expiry**;
  session revocation requires separate implementation/testing and must not be
  represented as an immediate all-device logout.
- Offline/local encrypted vaults cannot be read, wiped or remotely locked by
  the administrator. Restoring cloud login does not decrypt a user's vault.
- Session revocation via Supabase Auth Admin `signOut(jwt)` needs the
  **target user's access JWT**, which an operator must never obtain. Do not
  fake a global revocation with the operator JWT, or mutate undocumented
  provider tables. Research a vetted supported method; until then record a
  review case, not a false success.
- Destructive deletion and MFA resets require reliable user identity proof,
  retention/export options, account-owner notification and irreversible impact
  confirmation. Their automatic endpoints are **not enabled**.
- Admin case history includes minimal UUIDs, fixed reason categories, action
  name and state (no email field, passwords or vault entries).
- Support cases cannot target the Owner, even if an admin session is stolen.
  Only the Owner has admin access in the current system; per-agent scoped roles
  are deferred to Phase 10.

## Acceptance and rollback

- Unit and isolated full handler tests cover AAL1/non-admin/origin denial,
  owner protection, malformed inputs, fresh MFA and replay prevention.
- Staging must exercise service-only database RPCs, no fake production users.
- Before any migration/deployment, verify TypeScript compilation and document
  inability to atomically commit Auth+DB operations.
- Additive SQL migration `20261013190000_admin_support_cases.sql`.
  Rollback the Edge Function to previous version, then leave the audit table
  intact for incident review (do not delete history merely to roll back UI).
- **Phase 02 remains open** until real supported session revocation, safe
  deletion/identity review, safety regression tests and production acceptance
  are complete. Do not advance to Phase 03 before closure.

## Phase 02 incremental change: cloud login status display (2026-10-13)

The support panel can now read the target account's current `banned_until` from
the privileged Supabase Auth Admin API through the Edge Function's
`supportStatus` action, without exposing service credentials. Owner/non-admin
authorization and protected-target checks apply before this read. Unknown
provider timestamps fail closed to an **unknown** display state.

The UI refreshes this server-derived status on account selection and after
confirmed actions; manual refresh is also available. It does not infer the
current suspension state from prior audit cases. No new database tables,
production user mutations, or paid resources are needed.

**Important limitation:** this is not session revocation; JWTs already issued
may remain usable until expiry, and local encrypted vaults remain untouched.
Remaining Phase 02 acceptance items still block Phase 03.

**Rollback:** revert the Edge Function and HTML UI to the previous deployed
version; no SQL rollback is required for this read-only enhancement.

## Phase 02 — user-authorized global cloud session sign-out

An independent `/security` page on the **existing** Lockvyn admin UI host
allows a Lockvyn account owner (not an administrator acting as that user)
to authenticate with their **own** Supabase cloud credentials, verify a
registered TOTP factor when enrolled, and explicitly revoke **their own**
cloud refresh sessions via the supported GoTrue
`POST /auth/v1/logout?scope=global` endpoint.

- Password/TOTP/access JWT remain only in that browser page's memory. The
  page talks **directly to Supabase Auth**, not the privileged admin POST API;
  no service-role credential is embedded in the public HTML, Cloudflare Worker
  or client state. No tokens are stored in browser local/session storage.
- A separate final consent click is required. Auth failures report an error,
  never a fabricated success. The page uses nonce-scoped CSP, no third-party
  scripts, no analytics, no new service or paid feature.
- Revoking refresh tokens does **not** invalidate an already-issued access
  JWT before `exp`. It cannot lock an offline encrypted vault or recover a
  missing TOTP factor. Clients will need to sign in again after expiry.
- The admin's `session_review` workflow is still review-only. No admin may
  impersonate a customer or collect a user's password/JWT to remotely log
  them out. This is a **self-service mitigation**, not an administrator
  session revocation endpoint.
- Integration test: disposable local GoTrue users sign in from two additional
  sessions, the authenticated user initiates global sign-out, and old refresh
  tokens are rejected. Existing AAL2 owner, banned-user and AAL1 denial tests
  remain enabled.
- No SQL migration and no production user data changes. Roll back by
  reverting the Edge Function and Cloudflare Worker to their previous
  versions, leaving user Auth and encrypted vault records intact.

## Public production delivery smoke test

The existing Admin and release checks workflow now makes unauthenticated, read-only GET requests to both the public dashboard and the owner security page at /security. The smoke checks require HTTP 200, HTML, no-store and browser security headers, nonce-scoped inline scripts, the expected owner confirmation and offline-vault warning, and no browser storage or service-role material in the page.

This proves page delivery and its public response safeguards only. It does not sign in, test a real MFA factor, revoke a session, or perform an account action. It sends no email, password, TOTP code, token, or user data. Keep authenticated production acceptance as an open Phase 02 gate.

**Remaining Phase 02 gate:** production user acceptance, supported
identity-proofed 2FA recovery, user-consented deletion with retention, and
strict session invalidation for all sensitive application operations.
Do not mark this stage complete or start Phase 03 solely on this change.
