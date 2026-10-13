# Phase 02 — Account support actions (owner-directed closure; acceptance incomplete)

**Budget: $0.** Phase 02 was administratively closed on 2026-10-13 at the owner's direction so Phase 03 could begin. This records an explicit waiver/deferment; it does not claim every acceptance gate passed. The outstanding gates remain listed below for later follow-up.

## Implemented and verified

- The owner-only admin support workflow can suspend or restore a non-admin
  account's **cloud login** for a fixed period. It requires a fixed reason, a
  prepared single-use case, the exact target email, and a fresh TOTP step-up.
  Self and admin targets are protected. Requests are rate-limited and audited.
- The owner-facing `/security` page lets an account owner sign in directly to
  Supabase Auth and revoke their own refresh sessions after selecting and
  verifying an enrolled TOTP factor and confirming the action.
- Migration `active_cloud_sessions` is applied to the existing project and
  `lockvyn-admin` v12 is active. Sensitive Lockvyn admin, encrypted-snapshot,
  and device-pairing paths check the signed session ID against the live Auth
  session row, so those paths reject a revoked session even while an access JWT
  remains unexpired.
- PR #41's Windows, Android, encryption-compatibility, browser, and admin
  checks passed. The workflow's public-page smoke test is read-only and checks
  delivery/security headers; it does not authenticate as a real customer.
- Latest isolated local GoTrue/Postgres session invalidation checks and
  synthetic admin/MFA/support checks passed in workflow runs
  [38432588454](https://github.com/R3Dzf/BoshaVault/actions/runs/38432588454)
  and [38432588163](https://github.com/R3Dzf/BoshaVault/actions/runs/38432588163).
- These operations do not read vault contents, change a master password, or
  delete vault rows. Offline local vaults remain usable.

## Acceptance checklist

- [x] Keep cloud password recovery separate from the vault master password.
- [x] Fixed reason, fresh TOTP step-up, owner/admin target protection, audit,
  throttling, and replay protection for implemented admin support actions.
- [x] Suspend/restore cloud login without claiming to lock a local vault.
- [x] Provide owner-authorized global session sign-out and guard sensitive
  Lockvyn paths against revoked sessions; document the offline/JWT limits.
- [ ] Provide supported recovery if the owner loses **all** enrolled factors.
  A separately enrolled backup TOTP can be selected, but there is no admin
  bypass or verified all-factors-lost recovery flow.
- [ ] Implement owner-consented account deletion only after a clear retention
  and rollback policy is approved and tested. No automatic deletion endpoint
  is enabled.
- [ ] Complete authenticated production acceptance with a separate consenting
  non-admin test account. Public GET smoke checks are not this acceptance test.
- [ ] Complete final manual accessibility and recovery/deletion safety review.

## Deferred gates and safety limits

The unchecked items remain unresolved and can be resumed later. No credentials
or consent for a separate production test account were available. Recovery
codes are not enabled for this hosted project because server-side
support/configuration has not been verified. Do not weaken MFA or add an
administrative reset shortcut to close the checklist.

Admins cannot obtain another user's password, JWT, TOTP secret, vault key, or
master password. Admin session/deletion/MFA review cases do not execute those
actions. Global session sign-out is user-authorized self-service.

Supabase access JWTs remain cryptographically valid until expiry. The session
guard covers the listed Lockvyn-sensitive routes; other Supabase routes without
that guard may accept a JWT until expiry. Suspending cloud login does not lock
an offline vault. Restoring cloud login does not decrypt one.

## Evidence and rollback

- Support actions and replay protection were exercised against disposable local
  GoTrue/Postgres accounts before deployment.
- Session invalidation was tested against disposable local sessions. Tests
  verify that stale JWTs fail on sensitive paths and that encrypted snapshot
  and pairing rows remain present.
- Production migration history includes `active_cloud_sessions`; deployed
  function list reports `lockvyn-admin` v12. No production user account,
  MFA factor, or vault row was changed as part of these checks.
- The latest merged session-guard change is
  [PR #41](https://github.com/R3Dzf/BoshaVault/pull/41).
- For a code rollback, redeploy the previous Edge Function and use a forward
  migration to restore the prior RLS guard. Never delete Auth or encrypted
  vault rows to roll back access controls.
- This documentation records the owner-directed phase transition. It adds no
  migration and changes no runtime behavior.