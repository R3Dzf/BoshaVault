# Phase 09 — Privacy-conscious analytics

**Issue:** [#27](https://github.com/R3Dzf/BoshaVault/issues/27)
**State:** In progress by owner direction; acceptance and rollout gates remain open.
**Cost boundary:** Local Supabase and synthetic fixtures only. No paid staging, hosted migration, customer account, or production deployment.

## Scope
- Windows and Android expose account-level consent, default off, with a server refresh and opt-out.
- Consent is verified against the active confirmed account session. Opt-out removes the consent and all that account's event rows.
- Allowlisted events contain only platform, app version, event day, sync outcome, and coarse transfer-size bucket. Update transitions are inferred server-side. No email, vault data, device identifier, IP address, raw error, or exact byte count is stored in analytics.
- Admin reporting requires the existing confirmed-user, AAL2, active-session, and allowlist checks. Reports aggregate cohorts and suppress groups with fewer than five opted-in accounts.
- The dashboard has bounded date filters, an accessible aggregate table/chart, and CSV export containing only report fields.
- A daily pg_cron job purges events after 90 days and admin report reads after 180 days. Consent and event RPCs are rate limited.

## Verification
Local CI is the acceptance path: SQL fixture exercises consent, session/AAL denial, aggregation, cohort suppression, rate limiting, retention, and opt-out deletion; JavaScript tests cover input bounds and safe output; the local-auth workflow exercises Edge RPC integration; browser accessibility and Windows/Android build workflows cover client integration. Use only synthetic users and data.

This phase remains open until its issue checklist, exact-head CI, authorization review, staging/production decision, and rollback review are recorded. Prior owner-directed sequencing waivers remain incomplete and are not counted as passed phases.

## Rollback
The migration is additive. Rollback first unschedules lockvyn-analytics-retention, then removes the five public analytics RPCs, then drops private tables in dependency order (analytics_admin_reads, analytics_events, analytics_consents), then removes lockvyn_private only if empty. Keep the shared pg_cron extension because earlier phases use it. Removing these tables permanently deletes analytics events, consent records, and report-read audit history. A rollback must be reviewed against the target environment and backed up before execution. No production rollback or hosted migration has been executed.
