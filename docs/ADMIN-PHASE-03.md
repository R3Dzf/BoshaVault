# Phase 03 — opt-in cloud device sessions

## Implementation

Migration `20261013200000_cloud_device_registry.sql` adds a private registry for Lockvyn Auth sessions. A registry row means “this signed-in app session opted in”; it does **not** prove a physical device identity. Multiple sessions on one physical device may appear as separate entries. Client-reported platform, OS/app version, and display name are labels, not attested facts.

The database stores no IP address, MAC address, hardware fingerprint, vault identifier, snapshot contents, passphrase, encryption key, or recovery material. Auth session IDs stay in the private schema and are not returned by either owner or administrator inventory RPCs.

Android and Windows now offer a separate session-visibility choice. They register only after the user enables it and the next cloud sync succeeds; successful syncs update a server timestamp. Turning visibility off calls `lockvyn_unregister_cloud_device`, which deletes that session's inventory metadata and leaves cloud sync and offline data available. Sessions without consent remain compatible.

Owners can view or revoke their own sessions. The admin dashboard lists consented session metadata for non-admin target accounts. Administrator revocation requires an allowlisted account, an active AAL2 session, a TOTP verification within three minutes, and typing the target email. It rate-limits to 10 actions per minute per actor session and records actor/target session IDs in the private audit table.

Revocation marks the selected signed Auth session denied on encrypted snapshot and temporary pairing routes. It does not invalidate Supabase access or refresh tokens, erase encrypted snapshots, erase offline vaults, or remove data already downloaded to another device. Signing in again creates a new Auth session that can be opted in separately.

## Local synthetic verification

`supabase/tests/cloud_device_registry.sql` checks consent, metadata validation, owner isolation, heartbeat timestamps, AAL2 revoke, rate limits, per-session RLS, re-registration denial, audit, and ciphertext retention in a transaction that rolls back.

`supabase/tests/admin_device_access.sql` checks administrator allowlisting, protected targets, AAL2 and fresh-TOTP enforcement, email confirmation, cross-target rate limits, per-session access, and ciphertext retention using synthetic users and Auth sessions.

The local Auth workflow applies repository migrations to disposable local Supabase containers, runs both SQL fixtures, starts the local Edge Function, then runs `tools/test_admin_local_auth.mjs` with synthetic GoTrue accounts. That integration verifies opt-in, heartbeat, inventory, revocation of one of two separate sessions, continued access by the second session, metadata withdrawal, and retention of encrypted snapshots. It does not use a hosted Supabase project.

## Rollout boundary

These migrations and client/admin code are repository changes only. No hosted Supabase migration or app deployment is included. The merged code passed its local synthetic SQL, GoTrue, Edge Function, Android, Windows, and browser CI suites. A static accessibility/security review was recorded, and its focus-management findings are addressed in the current follow-up. Independent review and hosted non-customer staging remain open.

See [the Phase 03 rollout and rollback runbook](ADMIN-PHASE-03-ROLLOUT-ROLLBACK.md) for the migration order, staging gates, validation checklist, and forward-only recovery guidance. Do not roll back the revocation guard after any real session has been revoked: removing the guard would restore cloud access to those sessions. Preserve revoked session IDs and denial checks in a forward-compatible guard.
