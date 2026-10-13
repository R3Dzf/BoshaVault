# Phase 03 — rollout and rollback runbook

**Status (2026-10-13):** PR #44 and PR #45 are repository changes merged to `main`; no hosted Supabase migration or app deployment has been performed. The latest local CI applied the repository migrations to disposable Supabase containers and passed synthetic SQL and local GoTrue tests. This document is a safety plan, not evidence of a hosted staging rollout.

## Change order and dependencies

The migrations are additive and must be applied in timestamp order:

1. `20261013200000_cloud_device_registry.sql` creates the private, opt-in session registry and the restrictive checks on snapshot/pairing routes. Existing unregistered Auth sessions remain compatible; a registered and revoked session is denied on those Lockvyn routes.
2. `20261013203000_admin_device_access.sql` expands the private audit schema and adds service-role-only administrator inventory/revocation RPCs. The RPC rechecks allowlist membership, protected targets, the live signed Auth session, AAL2, fresh TOTP, target email, and the actor-session rate limit.
3. `20261013210000_cloud_device_unregistration.sql` adds user consent withdrawal and permits the corresponding audit action value.

The clients and Edge Function depend on these RPCs, so the database must be present before deploying the Edge Function and distributing clients. Turning visibility on remains optional; older clients and sessions that have not opted in continue to work.

## Preconditions for a hosted staging rehearsal

Use a separate non-customer Supabase project with a clean, synthetic-only dataset. Never point this rehearsal at the active customer project or import production Auth users, snapshots, secrets, or backups. Do not create a hosted project or paid branch without the owner's explicit organization and cost confirmation; the present evidence is from local self-hosted Supabase only.

Before applying migrations:

- Confirm the project reference against the designated staging record in two places; store it in a protected GitHub environment, not in source.
- Confirm Postgres/Supabase compatibility and that the project starts from the repository's migration history.
- Take and verify a recoverable pre-change backup using the target project's supported plan and rehearse the restore procedure separately.
- Record `supabase migration list` before the change and ensure no unreviewed schema drift exists.
- Keep the migration apply step manually gated. No secret, database URL, access token, JWT, email address, or user-provided metadata belongs in logs.

Apply the three files sequentially, then record the migration list and run the SQL fixtures on synthetic accounts. Run Supabase security/performance advisors on staging and inspect the effective grants and RLS policies. Verify the Data API can call only the intended RPCs: authenticated users can invoke their own registration, heartbeat, list, revoke, and unregister operations; service-role calls to administrator RPCs remain server-side. The migrations explicitly revoke broad function execution and grant the intended roles. Recheck this on the target project because Supabase is changing defaults for newly created Data API objects on October 30, 2026 ([Supabase changelog](https://supabase.com/changelog/45329-breaking-change-tables-not-exposed-to-data-and-graphql-api-automatically)).

After the schema checks pass, deploy the matching `lockvyn-admin` Edge Function, then test updated Android and Windows clients with synthetic accounts. Keep the feature disabled for users until this complete sequence passes.

## Go/no-go rehearsal checklist

Use two separate synthetic Auth sessions for one synthetic owner and a separate synthetic allowlisted admin. Verify all of the following before promoting any change:

- A session without consent never appears in inventory; consent withdrawal removes its inventory row and leaves sync access working.
- A revoked opted-in session cannot read or write snapshots or use pairing, while a second session remains usable.
- AAL1, non-admin, protected-target, stale/missing TOTP, wrong email, replay, and rate-limit cases are denied.
- Revocation records only the necessary owner/actor/device/session IDs and timestamps; it does not expose session IDs in the admin API or record vault content.
- Snapshot ciphertext and offline files remain intact after revocation and after consent withdrawal.
- Legacy sessions that never registered remain compatible.
- The Windows and Android UI describes the cloud-only scope and does not claim Supabase refresh tokens are revoked.
- Keyboard-only checks confirm the consent and revoke controls have visible focus; opening confirmation focuses the email field; Cancel restores focus to the invoking revoke button; completion focuses the updated live status; errors are announced without losing the form context.

The local workflow already covers the synthetic database/Auth cases above. The hosted staging rehearsal and a separate human screen-reader/accessibility review are still outstanding.

## Recovery and rollback rules

This feature is **forward-only at the database layer**. Do not create or run a down migration that drops the registry, audit fields, RPCs, grants, or restrictive RLS policies after a session may have been revoked. Dropping the guard would silently restore that still-valid Auth session's access to Lockvyn cloud routes.

- **Before the first migration:** stop the release; the live schema remains unchanged.
- **If a migration fails partway through the sequence:** stop promotion. Read the target's migration history and deployment logs, do not edit or delete an applied migration, and prepare a reviewed forward-fix migration. Each migration wraps its own DDL in a transaction; earlier successful migrations remain applied.
- **If Edge Function or client deployment fails after the migrations:** roll back the function/client code to the last compatible release, but leave the schema, registry, and RLS denial checks in place. Unregistered old sessions remain compatible.
- **If an opted-in session was revoked:** do not clear its revocation row as a rollback. Ask the user to sign in again to create a distinct Auth session, then opt that new session in if desired. The existing Auth refresh token is not invalidated by this feature.
- **If registry writes must be paused:** disable the UI or ship a forward code change to stop new opt-ins; keep existing revoked-session checks active and preserve audit records.
- **Restore from backup only for a separate, verified database incident:** rehearse on staging first and verify the restored migration history and revoked-session denial state. Do not restore a database snapshot over a live project merely to undo this feature.

A rollback is complete only when the code is back on a compatible version, revoked sessions remain denied, unregistered sessions retain the legacy path, encrypted snapshots/offline files are intact, and the staging migration history and smoke tests are recorded. No production rollback evidence exists yet.
