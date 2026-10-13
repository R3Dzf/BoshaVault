# Lockvyn Admin — operational checks and rollback

This runbook applies to the admin UI Worker and Supabase `lockvyn-admin` Edge
Function. It must stay up to date as each phase is released.

## Zero-cost staging

- The project owner requires **$0 total spending**, no paid cloud projects,
  paid Supabase branches, overage charges or subscription upgrades.
- The previously quoted Supabase hosted branch at $0.01344/hour is not
  authorized; do not create it.
- Run unit and integration tests against a **local synthetic handler harness**,
  with a locally hosted ephemeral Supabase stack only if feasible on a free
  runner. Never create fake production users or email real customers.
- Stop safely if hosted free quotas are exhausted. See
  [ZERO-COST-POLICY.md](ZERO-COST-POLICY.md).
- The AAL1/non-admin staging check must be evidenced. Synthetic/mock tests do
  not by themselves prove the live provider integration. Keep the phase
  open until an isolated free integration test has passed.

## Release gate

- Keep the [phase tracker](admin-phases.json), [roadmap](ADMIN-ROADMAP.md)
  and each phase GitHub issue aligned.
- Every branch must pass the admin permission tests, anonymized live denial
  probes, the password-manager crypto/regression suite, Windows/Android builds,
  and relevant browser tests on the **same commit**.
- Required staging checks: one allowlisted user with AAL2 can list only
  approved metadata; the same user with AAL1 is rejected; a non-admin with
  AAL2 is rejected. Never collect passwords or real bearer tokens in CI.
- Confirm the dashboard's own Cloudflare UI does not cache private account
  information or expose `service_role`, token strings or master passwords.
- Changes to the server API and frontend require a coordinated release and
  an explicit support smoke test before closing the phase issue.
- A failed or missing check means **hold deployment**, never "mark complete."

## Operator emergency actions

1. Stop applying privileged changes. Capture only request IDs / timestamps
   (no session tokens, MFA codes, passwords, or vault entries).
2. For an unauthorized user exposure: disable the admin Edge Function
   temporarily or roll back to the last passing version. Rotate exposed
   *server-side* credentials through Supabase and review audit logs.
3. Restore the last validated Cloudflare Worker deployment if its UI breaks.
   The previous Supabase Edge Function remains the source of truth for admin
   permissions; a static frontend cannot grant its own admin privileges.
4. Never uninstall apps or delete encrypted vault data to fix an admin issue.
5. Re-run deny-path tests before reopening the dashboard.

## Rollback / migration rules

- GitHub releases / signed Android packages and existing Velopack feeds are
  independent of admin dashboard frontend updates.
- Use additive database migrations first. Back up the schema before any
  destructive migration; write down an explicit rollback path and retention
  impact in the corresponding phase issue.
- Emergency security controls should fail closed and must be disabled safely
  when the admin backend is unavailable; never disable password encryption,
  device signature checks or user-visible approval dialogs.
- Only support **cloud** session revocation / sync suspension; offline vault
  data is neither remotely decrypted nor guaranteed erased.

## Phase evidence

For each GitHub issue record: reviewed commit SHA, test and security CI run
links, migration ID, staging observations, production deployment version,
rollout/rollback procedure, and remaining known limitations. Close the phase
issue only when its full checklist is satisfied.
