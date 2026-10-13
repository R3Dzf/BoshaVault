# Phase 04 — Security Center

## Scope

The dashboard feed is a metadata-only, append-only view over existing first-party admin/support/device audit events plus three verified-user admin access-denial signals. It contains event IDs, server timestamps, event codes, actor/subject UUIDs and a small enumerated detail code. It never copies emails, passwords, OTPs, recovery codes, access/refresh tokens, IP addresses, user agents, device IDs, vault contents or encryption material.

The event sources are:

- First opt-in registration of a cloud Auth session (a software session, not proof of physical hardware).
- Lockvyn cloud-session revocation performed by an authorized user or admin.
- Cloud account recovery request audit.
- Support case creation and final action outcomes.
- An account with a Supabase-verified JWT blocked for missing AAL2, an inactive Auth session, or missing admin allowlist membership. Repeated signals are deduplicated per actor and signal for two minutes.

Failed password sign-ins never reach the authenticated admin endpoint and are not captured. Supabase Auth audit logs can contain IP address and user-agent data, so this dashboard does not ingest those raw records. Do not describe the denial signals as a complete view of customer login failures.

## Access, alerts and privacy

- The security feed is returned only through the Edge Function after the request passes verified JWT, AAL2, active Auth session and server-managed admin allowlist checks. The database function repeats the actor, AAL2 and active-session checks.
- Database event tables are private, RLS-enabled and inaccessible through client roles, including direct service-role table reads. Triggers write the allowlisted event codes.
- The feed returns at most 100 events. The UI requests 50 and renders every dynamic field with `textContent`.
- Repeated verified-user admin denials by one actor (five or more in 15 minutes) raise a signal. New opt-in cloud sessions and failed support actions are shown as 24-hour counts.
- Events expire 90 days after the server timestamp. A daily `pg_cron` job purges expired rows; opening the feed also prunes expired data. Application roles cannot update or delete retained events.
- The existing support preparation/confirmation flow remains the incident-response path. This phase adds monitoring only; it does not add global logout, remote data deletion, password access or vault recovery actions.

## MFA and provider rate-limit boundary

Admin API access remains AAL2-only. Existing recovery, support and device-revocation rate limits remain enforced by their current server-side RPCs. This implementation does not change Supabase Auth provider rate limits or require MFA for customer accounts. Any customer-wide MFA or provider-throttle change requires a separately reviewed rollout and must not be inferred from these admin-denial alerts.

## $0 test path

The GitHub Actions `Local Supabase Auth staging` workflow starts a disposable local Supabase stack, applies the complete migration chain, runs synthetic SQL fixtures, starts the real local Edge Function and exercises real local GoTrue users/TOTP. It uses generated `example.invalid` accounts and rolled-back fixtures; no hosted project, customer account, paid branch, email provider or production setting is used.

Acceptance evidence is the passing final-head local Auth/migration run plus the admin handler/accessibility checks. A separate human screen-reader/security review and any hosted rollout remain external evidence; code CI does not substitute for those reviews.

## Rollout and rollback

Migration `20261013220000_phase04_security_center.sql` is additive. Review the local migration result, private grants, trigger events, retention schedule and denial cases before any hosted application. No hosted migration is part of this phase implementation.

If a rollout must be stopped, first roll back/hide the Edge Function and UI feature so requests no longer depend on the feed. Keep the event table and retained audit history intact; do not drop the audit table or triggers as an emergency rollback. If retention scheduling itself must be stopped, unschedule only `lockvyn-admin-security-retention` through `cron.unschedule(jobid)`, then preserve and review the audit rows. Re-enable only after the cause is fixed and the same local suite passes again.
