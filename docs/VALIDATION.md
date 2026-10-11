# Lockvyn build validation

Automated checks use synthetic credentials and disposable encrypted fixtures. They do not send a real confirmation email. See the latest [PR checks](https://github.com/R3Dzf/BoshaVault/pull/1) for the exact result of the current source commit.

| Area | Automated coverage |
| --- | --- |
| Vault core | Encryption, storage, concurrent writes and transport |
| Vault UX | Folders, review, deletion, generator rules and schema compatibility |
| Cloud clients | Mocked account requests, email-code handling, MFA factor selection, token binding and atomic encrypted merge |
| .NET / Java compatibility | Encrypted roundtrip in both directions and Unicode data |
| Native Android UI | 32 keyboard, email-code, MFA masking/QR cleanup and 1000-login navigation checks |
| Windows desktop | DPAPI storage, local TLS/Schannel, browser host protocol and approved extension registration |
| Email templates | All thirteen Arabic templates rendered by Go with synthetic variables |
| Windows updates | Smaller delta generation, complete reconstruction hashes, missing-base and corruption fallback, cancellation, version ordering, real installer/apply and encrypted-vault preservation |
| Downloads | Archive integrity, hashes, product labels, versions, icons and bundled setup guide |

The earlier Windows/Android and 32-check native Android suite passed in [build #176](https://github.com/R3Dzf/BoshaVault/actions/runs/38161121908). New Windows updater acceptance is mandatory before a Windows release can be published. Its synthetic versions 9.0.0 and 9.0.1 are not customer releases.

These checks do not replace physical PC/phone Wi-Fi transfer, camera, fingerprint or end-user email delivery acceptance. Windows files currently have no publisher Authenticode signature. Android CI packages without the permanent signing secrets use a temporary certificate, so users should export their encrypted backup before replacing a differently signed installation. No independent security audit is claimed.
