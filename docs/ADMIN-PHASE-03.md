# Phase 03 — opt-in cloud device sessions

## First implementation slice

Migration `20261013200000_cloud_device_registry.sql` adds a private registry for Lockvyn Auth sessions. A registry row means “this signed-in app session opted in”; it does **not** prove a physical device identity. Multiple sessions on one physical device may appear as separate entries. The client-reported platform, OS/app version, and optional display name are labels, not attested facts.

The database stores no IP address, MAC address, hardware fingerprint, vault identifier, snapshot contents, passphrase, encryption key, or recovery material. The Auth session ID stays inside the private schema and is not returned by the listing RPC.

Clients must first show an explicit consent prompt, then call `lockvyn_register_cloud_device` with consent and versioned metadata. Following a successful sync, an updated client can call `lockvyn_record_cloud_device_sync`; the server timestamps it and limits writes to once per minute. The owner can list their own records. Revocation requires the caller's live AAL2 session, changes the target session's server-side authorization, and records the actor/target session IDs in the private audit table.

The restrictive RLS policies check the signed `session_id` on encrypted snapshot and temporary pairing routes. Unregistered older sessions remain compatible. Once a session opts in and is revoked, that signed session can no longer use these Lockvyn cloud routes. The session's Supabase access/refresh tokens are not themselves invalidated. Signing in again creates a new Auth session; a newly updated client can opt that new session in. The UI must explain this behavior and must never describe the operation as token revocation.

Revocation leaves encrypted snapshot rows and every offline vault untouched. It cannot erase local copies or data already downloaded to another device.

## Local, no-charge verification

`supabase/tests/cloud_device_registry.sql` uses synthetic users, synthetic live Auth sessions, dummy ciphertext, and a transaction rollback. It checks explicit consent, version metadata, server heartbeat time, owner isolation, AAL2 revocation, continued use by a separate active session, revoked snapshot/pairing denial, audit creation, legacy-session compatibility, and ciphertext retention.

The `Local Supabase Auth staging` GitHub Actions workflow applies the prerequisite schemas and this migration to disposable local Supabase containers only, then runs the SQL fixtures. It does not contact a hosted Supabase project.

## Rollout and rollback boundary

This migration is additive and does not auto-register users or require old clients to register. Client consent UI, registration/heartbeat calls, the administrator inventory view, Android/Windows integration, accessibility review, and full production rollout remain open Phase 03 acceptance work.

Do not roll this migration back after any real session has been revoked: dropping the registry/policies would remove the server-side denial. A production rollback plan must preserve revoked session IDs and denial checks in a forward-compatible guard. No production migration is part of this PR.
