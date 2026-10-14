# Phase 10 — Scoped roles, operations & sign-off

**Issue:** [#28](https://github.com/R3Dzf/BoshaVault/issues/28)  
**State:** In progress by owner direction.  
**Cost boundary:** Local Supabase, synthetic identities, and free CI only. No hosted migration, paid resource, customer account, or production deployment.

## Authorization model

The existing lockvyn_admins allowlist remains the outer membership gate. An allowlisted user with no row in lockvyn_admin_roles is denied all admin actions by default. The server maps four roles to bounded capabilities:

- **Owner:** all supported admin actions plus role management.
- **Support:** account directory/recovery/support actions and ticket-agent actions.
- **Security:** opted-in device inventory, cloud sync controls, security events, and privacy analytics.
- **Release:** release policy and announcement management.

The Edge Function enforces each request, including ticket-agent requests. UI section hiding only reduces clutter and is not an authorization boundary. Unknown actions and missing roles fail closed.

## Owner bootstrap and role changes

The additive migration creates forced-RLS role and append-only audit tables. It does not promote any existing administrator. Once the migration has been applied, the first confirmed, allowlisted administrator can initialize the empty role set by signing in with AAL2, verifying a fresh TOTP challenge, and typing their own account UUID. A transaction advisory lock makes first claim single-winner. Before any hosted rollout, the operator must review the full existing allowlist and approve which account may perform this one-time claim.

Only an owner can assign or revoke roles. Targets must already be confirmed allowlisted accounts. Every change requires fresh TOTP and the target UUID typed for confirmation. The database prevents removal or demotion of the last owner. Audit rows contain actor/target UUIDs, role transition, session UUID, and timestamp; they exclude email, secrets, and vault data. UPDATE/DELETE are blocked by both grants and a trigger.

## Local verification

The exact-head pipeline adds a pure role-matrix test, SQL grants/RLS/bootstrap/step-up/last-owner/immutability fixtures, real local GoTrue role bootstrap and mutation checks, UI accessibility checks, a response/script payload budget with role-specific loading checks, and handler authorization regression coverage. Role integration uses throwaway .example.invalid accounts on the disposable local Supabase stack only.

## Operations, backup and recovery

Before applying the migration to any hosted project, capture a protected database backup using the project's approved encrypted backup process and record its retention/restore point. Restore drills must run against an isolated target; verify the role roster, audit rows, forced RLS, RPC grants and append-only trigger before considering the restore usable. Do not test restore by overwriting production. No hosted backup or restore drill has been run as part of this phase.

There is no hidden MFA bypass or emergency super-admin. If all owners are unavailable, pause privileged work and use the provider's documented, independently verified project-owner recovery process; review the allowlist, restore at most two trusted owners, and record the authorization in the incident log before resuming. If credentials or a service secret are exposed, disable the affected admin route, rotate the server-side secret in the provider, invalidate active sessions, review the append-only audit/security records, then rerun denial tests. Never rotate by placing secrets in source, logs, or the browser.

## Remaining gates

Technical implementation and CI evidence are tracked in #28. This phase remains open while independent authorization/threat-model review, an isolated backup/restore drill, operator rotation evidence, browser runtime performance measurement against an agreed budget, and the owner's hosted migration/production rollout decision with rollback evidence are outstanding. Phases previously waived for sequencing remain incomplete. Do not close #28 or mark Phase 10 complete while any of these checks is open.

## Rollback

The migration is additive. A rollback must be reviewed against the target schema and preserve the role-change audit. Do not drop role tables or disable the Edge role gate as a quick rollback after roles have been assigned. First restrict admin access at the Edge Function, restore the last known-good function, and only then plan a reviewed schema rollback from a verified backup. No production rollback has been executed.
