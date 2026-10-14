# Phase 07 — Announcements and notices

**Issue:** [#25](https://github.com/R3Dzf/BoshaVault/issues/25)  
**State:** In progress by owner direction. No hosted migration, production deployment, paid push provider, or public notice blast was performed.

## Scope and privacy

The admin dashboard supports bilingual English/Arabic drafts, text preview, publication, archive, severity, optional in-app alert preference, expiry, platform targeting, and Windows semantic-version or Android version-code bounds. Drafts stay in the private schema. Admin reads and writes require the verified-user, AAL2, active-session, and administrator allowlist gates; writes require fresh TOTP, typed notice ID for publish/archive, and a five-write-per-minute database limit.

The public feed is a bounded, sanitized response of active published notices for the requested platform, app version, and locale. It returns no account, installation, device, vault, credential, actor, IP, or read-receipt identifiers. A request includes only platform, version, locale, and the existing public API key. The app does not send sign-in tokens or device IDs.

Windows and Android store the alert preference locally, off by default. A manual in-app notices view remains available. When enabled, only in-app dialogs are shown; no operating-system notification permission or remote push provider is used. Local notice IDs suppress repeat prompts on that installation and are never uploaded. A failed offline fetch has no vault or sync effect. Windows retries at startup, on network availability, and periodically while opted in; Android retries when foregrounded and when connectivity returns.

Content is displayed as plain text. The feed is limited to 10 notices and 64 KiB; fields and response data are bounded. URLs are not included in the notice contract. Email-like identifiers, bearer tokens, JWT-shaped values, and UUIDs are rejected from admin-authored text.

## $0 verification path

- tools/test_announcements.mjs checks bilingual bounds, sensitive-marker rejection, platform/version validation, and locale values.
- tools/test-admin-handler.cjs tests the real Edge Function body with synthetic admin/non-admin tokens and a mocked feed. It checks public cache/CORS headers, malformed query rejection, AAL1 denial, draft access, invalid text, typed publish/archive, and no data writes for denied requests.
- supabase/tests/admin_announcements.sql uses only example.invalid synthetic users in a rollback transaction. It checks table/RPC grants, AAL2, active-session, allowlist, fresh-TOTP, draft invisibility, localization, platform/version matching, expiry, archive, and rate limiting against local Supabase.
- tools/test_admin_accessibility.py checks labels, live status, safe text rendering, and typed confirmation controls.
- Existing Windows and Android build/UI workflows verify both native clients compile and continue to pass their regression checks.

## Rollback and limits

The change is additive. To stop the feature, disable the Edge route and dashboard controls, then ship clients without automatic feed checks. Keep the private schema and audit history until a reviewed follow-up migration confirms they are no longer needed. Existing vault, sync, update, and offline behavior does not depend on the feed.

The phase remains open until exact-head CI passes and the owner reviews the result. Tests do not verify a hosted Supabase migration, production deployment, user-visible deployed dashboard, or an actual notification delivery. No announcement was sent to customers as part of implementation.
