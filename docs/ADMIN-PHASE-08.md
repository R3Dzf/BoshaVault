# Phase 08 — Support tickets

Phase 08 is in progress under the owner's sequencing instruction. Phase 06 (#24) remains open with its public release gate unverified. Phase 07 (#25) implementation merged, but its native reconnect/deployment gates remain open and the owner waived it only for sequencing. This phase may proceed, but cannot be closed as complete until dependency and acceptance gates are satisfied.

## Scope

- Signed-in users can create, list, read, reply to, reopen, and close their own in-app support tickets.
- Only verified active sessions can use the ticket API. User actions accept normal signed-in assurance; agent actions require an allowlisted administrator, AAL2, a fresh TOTP, an active session, and typed confirmation for state-changing operations.
- Agents can assign tickets to allowlisted administrators, change status/priority, escalate, and reply. Ticket threads are visible only to the requester and authorized agents.
- Ticket text is length-limited and redacted for common credential patterns before storage. Diagnostic attachments are optional, require an explicit unchecked-by-default consent control, and accept only a small allowlisted JSON object (platform, app version, OS version, and error code). Arbitrary files, vault data, device IDs, email addresses, and auth credentials are rejected or excluded.
- Tickets and diagnostic payloads expire after 180 days. Queries opportunistically purge expired rows; a production scheduled purge remains a rollout gate.

## Abuse and audit controls

- Per-user ticket creation and reply limits, per-admin mutation limits, row-level security, service-role-only RPCs, ownership checks, and a bounded 100-ticket admin queue.
- Append-only metadata audit entries omit message and diagnostic bodies. A separate diagnostics-upload event stores only consent state and field count; the diagnostics payload is never copied to audit.
- Ticket UUIDs are request handles; client responses omit requester UUIDs and admin email addresses.
- No email notifications, paid provider, hosted migration, customer data, or production deployment are part of this work.

## Rollback

The database change is additive. Rollback drops the Phase 08 RPCs, then drops the tables support_ticket_audit, support_ticket_attachments, support_ticket_messages, and support_tickets in dependency order. This permanently deletes ticket data, so production rollback requires an explicit owner decision and export/retention review; no rollback is run as part of local testing.

## Verification gates

Local SQL fixtures and synthetic Edge/client checks must cover ownership, AAL1 user access, AAL2/fresh-TOTP admin writes, consent, redaction, assignment, escalation, replies, rate limits, retention, and safe attachments. Exact-head Windows/Android and local Supabase workflows are required. Real client reconnect and production release/deployment verification remain external gates.
