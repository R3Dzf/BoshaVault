# Phase 06 — Release Manager

**Issue:** [#24](https://github.com/R3Dzf/BoshaVault/issues/24)  
**State:** Implementation in progress. No release, hosted migration, or production deployment was performed.

## Scope and safety

The dashboard reads published GitHub release metadata from the fixed `R3Dzf/BoshaVault` repository. It displays stable tags, bounded changelog text, public CI check status, asset sizes and GitHub SHA256 digests. Android signing status is reported from the published `android-update.json`; clients still compare the signer fingerprint with the installed app. Windows Authenticode signing is not configured and is shown as unsigned. Unknown CI status remains unknown.

The existing `windows-v*` and `android-v*` feeds stay in place. Release policy defaults to the latest stable feed, minimum version `0.0.0` / `0`, and 100% rollout. A policy may pin a published stable tag, set a minimum version, or reduce the rollout percentage. A candidate can be selected only when its GitHub checks pass; Android candidates must match the latest published signer fingerprint.

The policy table and audit history live in `lockvyn_private`. No direct Data API grants expose either table. Writes require the existing Edge Function's verified user, AAL2, active session and administrator allowlist, plus a fresh TOTP, a typed candidate tag (or `LATEST`), an enumerated reason, and the SQL-side five-writes-per-minute limit. The public GET route returns only the platform's candidate, minimum version, rollout percent, and update time.

Windows and Android clients fetch policy before offering an update. Rollout sampling is local to each check; the client sends no install ID or telemetry. Clients below the configured minimum can receive the candidate despite a paused rollout. Both clients reject downgrades. A rollback sets optional rollout to 0; the recovery path is a higher-version hotfix, not an automatic downgrade. The offline app and encrypted vault remain usable if policy lookup fails.

## Test path

- `node tools/test_admin_release_manager.mjs` covers version bounds, candidate tags, staged rollout, minimum-version override and no-downgrade behavior.
- `node tools/test-admin-handler.cjs` exercises the actual Edge Function with synthetic users and mocked GitHub release metadata; no network or database writes are performed.
- `supabase/tests/admin_release_policy.sql` tests private-table grants, AAL2/fresh-TOTP/admin/session checks, audit entries, throttling, and rollback against local Supabase only.
- The existing Windows and Android build/release checks continue to cover delta reconstruction, APK signer continuity, hashes, and legacy update feeds.

## Open acceptance gates

The issue remains open until exact-head CI passes and the final review confirms release metadata and local database behavior. This implementation does not prove a future hosted migration, future signed Windows publisher package, actual public release, or production rollout. Phase 02–04 waivers and deferred gates remain unchanged.
