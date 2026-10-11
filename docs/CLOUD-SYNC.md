# Optional encrypted cloud sync preview

Windows **1.8.0-cloud-sync-preview** and Android **1.4.0-cloud-sync-preview** include manual Supabase synchronization under **Devices & encrypted backup → Optional encrypted cloud sync**. It is off until you connect a cloud account. Local Wi-Fi transfer, encrypted file backups and offline vault use remain independent.

The dedicated project is **Orchid Ledger** in the **New Generation** organization, region `eu-central-1`, reference `ghmbcfqiomggoxgwghjs`. [Open its dashboard](https://supabase.com/dashboard/project/ghmbcfqiomggoxgwghjs). Source contains only the project URL and public publishable key; no administrative or service-role key belongs in either client.

## Connect your two devices

1. Export an encrypted local backup. Use synthetic entries for the first native-device test.
2. Both devices must already hold the **same vault**. Create it on one device, then transfer it over local Wi-Fi or import its encrypted backup on the other. Cloud enrollment cannot replace an unrelated vault or initialize a new device from cloud alone.
3. On the first device open the cloud screen, enter your email and a **separate cloud password of at least 16 characters**, and choose **Create cloud account**. This application account inside Orchid Ledger is separate from your Supabase dashboard login. Confirm its email, return to the app, enter the cloud password and choose **Start a new cloud vault**.
4. Choose **Show & copy connection code** on Windows, or **Copy connection code for your other device** on Android. This `BVCLOUD1:` code contains a random recovery key and binds your cloud account, local vault and cloud space. It contains no refresh/access token. Keep it private and transfer it only to your own trusted device. The app clears its owned clipboard after 20 seconds where the OS permits.
5. On the second device sign in with the same application account, paste the code in **Other device's cloud connection code**, and choose **Join this vault's existing cloud**. Starting separate spaces on both devices does not connect them.
6. Press **Sync now** on the first device, then the second, then the first again. Each sync downloads, authenticates and merges device snapshots locally before uploading this device's current encrypted snapshot. Concurrent edits keep conflict copies for review. Sync is manual, without a background scheduler.

Supabase's default email sender currently permits only organization-team addresses and has restrictive rate limits. For this private preview, use the email associated with the new Supabase account. Other recipients need a custom SMTP sender first; confirmation remains enabled. See [Supabase SMTP documentation](https://supabase.com/docs/guides/auth/auth-smtp). Signup requests use an S256 PKCE challenge, so the confirmation redirect does not carry access/refresh tokens; the app signs in separately with the password after confirmation. Real email delivery and confirmation UI still need native testing.

## Encryption and settings

The existing `.boshavault` file is already encrypted. A device additionally seals the **entire file** with AES-256-GCM under an independent random 256-bit cloud recovery key. Outer AAD binds format version, cloud space and device slot. Supabase receives only outer ciphertext, its checksum and random identifiers. Account email, network/access metadata, file sizes and upload times remain observable.

Each device writes its own row. Owner-scoped RLS checks `auth.uid() = owner_id` for SELECT, INSERT, UPDATE and DELETE, including UPDATE's `WITH CHECK`; anonymous table access is denied. SHA-256 detects transport corruption; AES-GCM authentication determines whether the file is accepted. Server timestamps are status metadata, not security counters.

Windows saves refresh tokens and the recovery key using **DPAPI CurrentUser** in `cloud-profile.dpapi`. Android uses app-private `cloud-settings.enc`, protected by an **Android Keystore AES-GCM key**; platform backups are disabled. Access tokens are transient. Rotated refresh tokens are saved before the next network request. These protections cannot prevent compromised apps/OS accounts from using their device's keys, and managed strings cannot be guaranteed erased from memory.

Every downloaded outer snapshot and inner vault is validated before one atomic local merge. A malformed, wrong-key, wrong-vault or mismatched-epoch batch changes no local file. If upload fails after a successful merge, the merged local file remains; retrying uploads its current state. Cloud outage, TLS failure, quota or expired sign-in never prevents offline vault access.

## Passphrase changes and disconnecting

Changing the master passphrase rotates the inner vault key. The preview refuses cloud snapshots from another key epoch. Merge **all devices** over local Wi-Fi or encrypted backups using the incoming passphrase first. Then disconnect cloud on both devices, start a **new cloud vault** on one, and join it on the other with the new code. Old cloud rows remain in their old space and do not block the new one. Preserve an encrypted backup and old code if you still need those snapshots.

**Disconnect this device** revokes this Auth session's refresh token and deletes local cloud settings. Already issued access tokens follow their normal expiration. **Forget local cloud settings (offline)** deletes settings only and cannot revoke the server session. Both actions retain local vault files and cloud snapshots.

Remote per-device management, mandatory Auth MFA, automatic key redistribution and cloud-only recovery/bootstrap are not implemented. Losing the recovery code can make existing cloud snapshots inaccessible without affecting local files. Starting a new space creates a fresh cloud key, but cannot erase copies already downloaded by a lost device. Project administrators can delete or roll back ciphertext; revision vectors preserve known local edits, but there is no independent server-history freshness checkpoint or availability guarantee.

## Limits and verification

- Up to **2,000,000 bytes** per inner snapshot; larger vaults are refused without truncation. Download is capped at 64 rows and 16 MB total response; Auth responses at 64 KiB.
- `supabase/migrations/20261009182200_opaque_device_snapshots.sql` is deployed to Orchid Ledger. Security advisors returned no findings at validation time.
- `supabase/tests/owner_isolation.sql` covers transactional owner CRUD, cross-owner read/mutation rejection, absent identity, anonymous privileges and server timestamps, then rolls back all fixtures.
- Temporary synthetic users exercised real Auth and the Data API: encrypted upload/download, cross-owner SELECT/INSERT/UPDATE/DELETE, anonymous/invalid-JWT rejection, refresh rotation and sign-out. All test users and snapshots were deleted afterward.
- .NET and Java tests verify shared envelope/connection-code formats, tampering, identity binding and non-destructive batch merge. CI also runs Windows DPAPI checks and builds both clients.

Native Windows/Android UI, Android Keystore lifecycle, email confirmation, cancellation/backgrounding and actual two-device cloud edits still require acceptance testing. Successful automated checks are not an independent security audit. See [validation record](VALIDATION.md) and [security model](SECURITY.md).

Run `dotnet run --project src/BoshaVault.Tests -c Release -- --cloud-tests`. Native DPAPI checks use `--cloud-store-tests` on Windows. The CI `cloud-interop` job creates fake fixtures, runs `tools/CloudInteropTest.java`, and verifies Java ciphertext in .NET. Opt-in `--cloud-live-tests` takes a private configuration file for two temporary confirmed Auth users; it is excluded from normal CI and must never use personal vaults.
