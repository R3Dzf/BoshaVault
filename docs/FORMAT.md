# BoshaVault envelope v1 / payload schema 2

All text is UTF-8. Master passphrases are NFC normalized before Argon2id. Both implementations reject files over 16 MiB, duplicate properties and unsafe KDF parameters. KDF settings are fixed for this format: Argon2id v1.3, 65536 KiB, 3 passes, 4 lanes, 32-byte output, 16-byte salt. `keyEpoch` is a positive integer, initially 1, advanced on each master-key rotation.

Root properties: `format` (BoshaVault), `version` (1), `vaultId` (lowercase UUID), `keyEpoch`, `kdf`, `wrap`, `payload`. KDF fields: `name`, `memoryKiB`, `iterations`, `parallelism`, `salt`. Each cipher box has canonical padded Base64 `nonce`, `ciphertext`, `tag`; 12-byte nonce and 16-byte tag. Wrapped plaintext is the 32-byte random vault key. Payload schema 2 is encrypted JSON containing `schema`, `vaultId`, `revision`, `entries`, `trustedApps`, `folders`. Envelope version, encryption, AAD and key epoch semantics remain version 1.

Associated data is the UTF-8 encoding of the following exact strings; `<...>` denotes an inserted value, not a literal bracket:

```
BoshaVault|1|<vaultId>|<keyEpoch>|argon2id|65536|3|4|<salt>|wrap
BoshaVault|1|<vaultId>|<keyEpoch>|argon2id|65536|3|4|<salt>|payload|<wrap.nonce>|<wrap.ciphertext>|<wrap.tag>
```

The serialized order of JSON properties is not part of the authentication format. The exact Base64 wrapper values are authenticated. No externally supplied cryptographic algorithm or arbitrary KDF cost is accepted.

Entries contain a lowercase UUID `id`, `title`, `username`, `password`, `url`, `notes`, `folder`, `favorite`, `deleted`, UTC `updatedUtc`, and an entry `clock` mapping device UUIDs to positive integers. Local edits increment their device component. Dominating clocks supersede older edits. Incomparable different fields generate a deterministic conflict-copy UUID from SHA-256 of entry ID and sorted serialized clock vectors; neither edited secret is silently discarded. Trash entries with `deleted=true` can be restored. Schema 2 adds `purged` and `reviewAcknowledgement` to every entry. Permanent removal uses `purged=true`, `deleted=true`, generic title `Deleted login`, empty username/password/url/notes/folder/review acknowledgement, and `favorite=false`. Only identity, clock and timestamp remain. A permanent removal always defeats a stale or concurrent edit of that identity; it never creates a credential-bearing conflict copy. Local and synchronized permanent removal delete the previous automatic local snapshot. Previously exported backups and old per-device cloud rows cannot be retroactively erased by this operation.

Trusted apps contain `package`, `certificate` (SHA-256 of the single current APK signing certificate, lowercase hex), `browser`, `entryIds`. Native app approvals are scoped to IDs; browser approvals only authorize reporting an origin and never replace exact-host filtering. Merge retains local app approvals rather than adopting remote ones.

This custom envelope/merge protocol is interoperable in the included tests; that is not a claim of public standardization, formal verification, rollback resistance or third-party review.

## Schema 2 metadata and older vaults

`folders` is an encrypted list of distinct names, 1–100 characters each, up to 10,008 names (the entry limit plus presets). The default choices are Personal, Work, Email, Finance, Social, Shopping, Gaming and Other. Empty custom folders persist. Merge unions folder catalogs; rename/removal of folder entities is not implemented. Entry folder assignment and folder filtering do not replace Favorites, Review or Trash.

`reviewAcknowledgement` is either empty or 64 lowercase hexadecimal SHA-256 characters. It hashes `BVREVIEW1|<entry-id>|<base64-UTF8-password>|<base64-UTF8-username>|<semicolon-joined-warning-reasons>|<reuse-group-signature>`. Reasons are ordered: short/missing, reused, common, predictable. The reuse-group signature is SHA-256 of the comma-joined, ordinal-sorted IDs sharing the same nonempty password, or empty if no reuse exists. The fingerprint stays inside the encrypted payload. Acknowledgement hides the current review finding without improving the password. Changed password, username or reuse membership invalidates it; a title/notes/folder change alone does not.

New clients read schema 1, add metadata defaults and derive the folder catalog, then write schema 2 on the next saved change. The original file is not rewritten merely by opening it. Older clients refuse schema 2. Update Windows and Android together before exchanging newly saved files. An exported pre-upgrade backup remains independently usable with its original compatible client; importing it into an updated existing vault still respects known permanent deletion markers.
