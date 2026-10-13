# Phase 05 — Encrypted sync and quotas

## Scope and limits

Cloud snapshots remain end-to-end encrypted. Supabase stores and counts the ciphertext only; Lockvyn does not decrypt snapshots for quota enforcement, admin reporting, or sync health.

- Default account limit: 64 MiB of ciphertext across at most 64 snapshots. A snapshot is capped at 2,000,029 raw bytes.
- The database derives each snapshot's raw byte count from its base64 ciphertext. It never trusts a client-provided count.
- Uploads take a per-owner transaction advisory lock, then check account usage and row count before insert/update. The database rejects the entire write with a bounded quota error; it never evicts an older snapshot.
- Sync outcome records are available only for sessions with explicit cloud-session inventory consent. They contain an allowlisted success/failure result, ciphertext byte count, server timestamp, and optional bounded error code. Writes are limited to one per minute per Auth session. No content, credential, IP, user-agent, or hardware identifier is collected.
- Owners can read their own usage and pause status. Admins can filter consented software-session metadata by platform, result, time window, and device/app label.
- A pause is scoped to encrypted cloud snapshot sync. A restrictive RLS policy blocks snapshot reads and writes while suspended, and the upload trigger returns a safe paused-sync error. Resuming removes only the suspension flag. Neither action deletes local vault data, ciphertext, or audit events.
- Admin controls require a verified active AAL2 allowlisted session, a fresh TOTP challenge, typed target email, and a server-side rate limit. The append-only admin audit is mirrored into the 90-day Security Center feed. Private quota, suspension, and audit tables are not directly accessible to API roles.

## Client behavior and compatibility

Windows and Android clients call the owner-only sync-status RPC before snapshot access. A paused response stops network sync and explains that the offline vault and encrypted snapshots remain preserved. Upload errors map only the database's bounded `BV001` quota and `BV002` pause codes to user-facing messages.

Existing clients remain compatible with the added derived `ciphertext_bytes` column because snapshot uploads continue to omit it. The database trigger computes it. Older clients do not call the sync-status RPC, so an administrator suspension still blocks their snapshot RLS access without deleting rows; upgrading gives them the clearer pause message. The default behavior for accounts without a suspension row is unchanged.

## Local evidence

The `Local Supabase Auth staging` workflow uses disposable local GoTrue/Postgres and synthetic `example.invalid` identities. The Phase 05 SQL fixture rolls back and checks:

- Old-client inserts and server-derived ciphertext sizes.
- Configured byte quota and the 64-row cap, including rejected writes leaving previous ciphertext unchanged.
- Opt-in success/failure metadata, bounded error codes, admin AAL2/allowlist checks, typed email and fresh-TOTP controls.
- Platform/outcome/device filters, private table grants, pause/resume auditing, restrictive RLS, and retained ciphertext.
- Owner-only status and the rate-limited outcome update.

The local GoTrue contract test additionally checks concurrent synthetic uploads, older request payloads, admin filtering, wrong-email rejection, pause/resume through the real local Edge Function, session RLS and ciphertext retention. Windows/Android build and compatibility workflows, admin handler tests, and the dashboard accessibility/static checks run on the same pull request head.

No hosted Supabase project, customer data, paid staging, or production deployment is used as part of these checks. CI evidence will be linked to issue #23 before the phase is recorded as complete.

## Rollback and recovery

The migration is additive. If a rollout is stopped, first revert the Edge Function, dashboard, and clients to compatible builds while retaining the additive schema, suspension records, and audit history. Do not drop the ciphertext column or delete snapshot rows as a rollback step.

If a cloud sync suspension was applied, resume it through the authenticated admin control flow with fresh TOTP and typed email. Keep quota enforcement enabled unless the migration itself is faulty; any database rollback must be a reviewed follow-up migration after checking active suspensions, snapshot counts, and retained audit evidence. No hosted migration has been applied by this implementation.
