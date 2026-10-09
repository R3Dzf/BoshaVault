# BoshaVault wire format v1

All text is UTF-8. Master passphrases are NFC normalized before Argon2id. Both implementations reject files over 16 MiB, duplicate properties and unsafe KDF parameters. KDF settings are fixed for this format: Argon2id v1.3, 65536 KiB, 3 passes, 4 lanes, 32-byte output, 16-byte salt. `keyEpoch` is a positive integer, initially 1, advanced on each master-key rotation.

Root properties: `format` (BoshaVault), `version` (1), `vaultId` (lowercase UUID), `keyEpoch`, `kdf`, `wrap`, `payload`. KDF fields: `name`, `memoryKiB`, `iterations`, `parallelism`, `salt`. Each cipher box has canonical padded Base64 `nonce`, `ciphertext`, `tag`; 12-byte nonce and 16-byte tag. Wrapped plaintext is the 32-byte random vault key. Payload plaintext is a JSON body containing `schema`, `vaultId`, `revision`, `entries`, `trustedApps`.

Associated data is the UTF-8 encoding of the following exact strings; `<...>` denotes an inserted value, not a literal bracket:

```
BoshaVault|1|<vaultId>|<keyEpoch>|argon2id|65536|3|4|<salt>|wrap
BoshaVault|1|<vaultId>|<keyEpoch>|argon2id|65536|3|4|<salt>|payload|<wrap.nonce>|<wrap.ciphertext>|<wrap.tag>
```

The serialized order of JSON properties is not part of the authentication format. The exact Base64 wrapper values are authenticated. No externally supplied cryptographic algorithm or arbitrary KDF cost is accepted.

Entries contain a lowercase UUID `id`, `title`, `username`, `password`, `url`, `notes`, `folder`, `favorite`, `deleted`, UTC `updatedUtc`, and an entry `clock` mapping device UUIDs to positive integers. Local edits increment their device component. Dominating clocks supersede older edits. Incomparable different fields generate a deterministic conflict-copy UUID from SHA-256 of entry ID and sorted serialized clock vectors; neither edited secret is silently discarded. Tombstones are ordinary encrypted entries with `deleted=true` and can be restored. They retain encrypted contents until a later explicit product deletion policy is introduced.

Trusted apps contain `package`, `certificate` (SHA-256 of the single current APK signing certificate, lowercase hex), `browser`, `entryIds`. Native app approvals are scoped to IDs; browser approvals only authorize reporting an origin and never replace exact-host filtering. Merge retains local app approvals rather than adopting remote ones.

This custom envelope/merge protocol is interoperable in the included tests; that is not a claim of public standardization, formal verification, rollback resistance or third-party review.
