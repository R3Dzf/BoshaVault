# Admin Phase 01 — Overview and account directory

This stage replaces page-only account search with **database-backed,
read-only directory queries**. It does not alter user accounts or encrypted
password-vault data.

## UX
- Summary cards use *all registered accounts*, not current page results:
  total, verified email, enrolled verified MFA, and last sign-in in 30 days.
- Account directory search is case-insensitive and covers all users.
- Email (all/verified/pending) and MFA (all/enabled/off) filters apply in
  the backend, before pagination.
- 30 rows per page. Count shows matching accounts separately from total.
- Loading, empty, failure and mobile states do not pretend missing data is zero.
- Password recovery remains explicit, user-owned, auditable, and separate from
  vault master-passphrase encryption.

## Security and privacy
- `lockvyn_admin_directory` is callable **only by service_role**;
  `anon` and `authenticated` are explicitly denied direct RPC execution.
- The RPC also checks that the authenticated actor belongs to the admin
  allowlist; Edge Function verifies the signed JWT, AAL2 and membership before
  making that RPC.
- Returned fields are limited to user ID, email, signup time, last sign-in,
  email verified, MFA enrollment, and whether it is the operator's own account.
- Password hashes, tokens, factor secrets, recovery links, user metadata and
  encrypted vault entries are never returned.
- Search is bounded (120 characters), page is bounded (1–10000), and page
  size is fixed at 30.
- No advertising or invisible tracking is introduced.

## Zero-cost and validation
- No new hosted Supabase projects or paid branches.
- Use the existing free database and serverless allocation for production.
- Integration tests run against disposable local GoTrue, Edge Function and
  Postgres containers in a public-repository standard GitHub Actions runner.
- Validate case-insensitive search, counts, filters and page offsets with
  synthetic users before any production database migration.

## Deployment / rollback
1. Apply additive migration
   `supabase/migrations/20261013173000_admin_directory.sql`.
2. Deploy the updated `lockvyn-admin` Edge Function after passing CI.
3. Cloudflare's HTML host continues to load the Supabase-provided UI with
   a nonce CSP. No Worker secrets or extra hosting are needed.
4. If a problem appears, revert the Edge Function to the earlier build,
   keeping the RPC (unused, read-only, service-role-only) in place. Remove the
   RPC in a later audited migration if needed, after confirming no consumers.
5. Confirm the dashboard from the permitted owner and ensure
   AAL1/nonadmin access still fails.
