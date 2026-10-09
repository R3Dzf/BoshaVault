# Optional Supabase end-to-end encrypted synchronization — engineering status

**Not enabled in user-facing apps yet.** The database migration, independent outer-encryption codec, user-JWT REST transport and deterministic tests are implemented, but these are building blocks rather than an operational sync feature. No Supabase project was created or modified. We must select/create a dedicated project with the user's explicit organization and cost approval, implement Supabase Auth + secure token storage on Windows/Android, device enrollment, cross-device conflict-safe orchestration and live two-user RLS tests before turning it on.

## Why the project name does not make data safe

A fake project name, opaque REST table names and random slots may reduce casual identification, but do not stop an attacker. Supabase API endpoints and publishable keys are expected to be discoverable. Security depends on the authenticated user's JWT, RLS and encryption. Never include a `service_role`, a secret API key, the database password, the vault passphrase, or the cloud recovery key in the app or GitHub repository.

The recommended architecture uses **two independent encryption layers**:

1. Local `vault.boshavault` keeps current Argon2id passphrase wrapping + AES-256-GCM body protection.
2. The complete local encrypted file (including its visible header) is **sealed again** with a separate **random 256-bit cloud recovery key** using AES-256-GCM. Associated data binds random `vault_slot` and `device_slot` UUIDs.
3. Supabase receives a base64 encoding of **the outer ciphertext only**, plus its hash, unrelated slot UUIDs, the Supabase Auth owner UUID, ciphertext length and timestamp. Its dashboard cannot inspect usernames/passwords/URLs, or even the raw local vault envelope, without the independent cloud key. File sizes, event times, account identity, IP/network metadata and access records still remain observable.
4. Each device uploads a separate encrypted snapshot under its slot. This avoids directly overwriting another device's edits. Download and merge must run **on-device**, via existing authenticated vault `MergeEncrypted`/`MergeUsingPassword` rules and explicit conflict handling. Successful merge then uploads a new device snapshot.
5. A cloud recovery key is generated locally and **never** uploaded. Both devices must receive it via a carefully verified local pairing/QR flow or manual entry. Losing it prevents decrypting cloud backups, but does not delete local backups. Rotating that key after losing a device requires resealing all cloud snapshots; revoking Supabase credentials alone cannot wipe previously downloaded data.

## Data model and access

`supabase/migrations/20261009182200_opaque_device_snapshots.sql` creates `public.opaque_device_snapshots` with owner-isolated RLS:
- `owner_id` references `auth.users`, all CRUD policies require `auth.uid()=owner_id`.
- `vault_slot`, `device_slot` are random UUIDs distinct from the actual encrypted vault ID and device ID.
- `ciphertext_base64` is the **outer-encrypted** binary only, *not* a plain base64 of the existing `.boshavault` file.
- `ciphertext_sha256` is for corruption/transport detection; **AES-GCM authentication** is required before accepting a snapshot.
- `updated_at` server-generated; cannot be trusted as an ordering/security counter.

No anon role can read or write the table. RLS should be independently tested using two accounts, including invalid JWT, expired sessions, cross-owner INSERT/UPDATE/SELECT/DELETE and Auth MFA (AAL) configurations. Supabase backup/replication copies remain ciphertext but can remain after user deletion according to provider retention.

**Preview restriction:** up to 2 MB per inner encrypted vault snapshot. Larger files must be handled later via a private Supabase Storage bucket with audited Storage RLS and resumable uploads. Cloud sync MUST refuse larger files, never silently truncate them.

## Identity, device trust and usability

- Use Supabase Auth, preferably passwordless email + optional mandatory MFA for syncing; the provider will know the Auth account identifier/email.
- On Windows, persist refresh tokens only if encrypted with Windows DPAPI using user scope; on Android use platform Keystore-protected encryption. Never persist access tokens or recovery keys in plaintext `desktop.json` or app logs.
- Require explicit device pairing/approval, provide **Devices → Revoke** plus login-session revocation and key rotation guidance.
- Keep **Local Wi-Fi transfer** independent, usable without Internet, Supabase account, subscriptions or cloud setup.
- Default **Cloud Sync OFF**. Once fully implemented, add Settings > Sync options: Local Wi-Fi / Encrypted cloud / Both. Clearly show last successful verified merge, device pending changes and errors without disclosing credentials.
- Failure is fail-closed and non-destructive: preserve both local and cloud encrypted backups. Do not assume last writer wins; protect against whole-file replay/rollback and conflicts.
- Quotas, TLS failure, sign-out, server outage or lost token must never prevent the offline vault from working.
- Complete a separate security audit of external data flows and device key handling before storing production passwords.

## Implemented source (not live connection)

- `src/BoshaVault.Core/CloudSnapshotCodec.cs`: random recovery key + AES-GCM outer envelope.
- `src/BoshaVault.Core/SupabaseSnapshotTransport.cs`: authenticated REST upload/download of outer ciphertext only; access token injected by future Auth layer.
- `src/BoshaVault.Tests/CloudTransportTests.cs`: fake HTTP transport tests (no network, no real keys or personal data).
- `src/BoshaVault.Tests/Program.cs`: outer envelope tamper, wrong key, wrong vault/device slot, transport checksum verification.

**Next blocked step:** create or select a *dedicated* Supabase project and obtain the user's explicit organization and project-cost approval. Do not deploy this migration into the user's Portfolio, Learning Platform, Resume or any other unrelated Supabase project.
