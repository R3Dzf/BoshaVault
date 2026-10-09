# BoshaVault security + UX release roadmap (2026-10-09)

**Release rule:** Source files and successful CI builds are NOT proof of production readiness. Run manual tests on Windows 11, TECNO/HiOS Android, Chrome and Edge before using real accounts; obtain independent security review of handling secrets, native messaging and browser injection. Keep a recoverable encrypted backup before upgrading.

| Capability | Engineering status | Required proof before enabling for users |
|---|---|---|
| Existing LAN Wi-Fi transfer | Implemented, unchanged | Real two-device pair/merge tests, reconnect, revoked pairing |
| Browser Autofill + generator + reviewed save | Windows 1.4 preview implemented | Multi-site signup/change-password, SPA, focus/navigation, browser restart, phishing failures |
| Offline password hygiene report | Core + Windows settings UI implemented | Site list sample, false-positive/false-negative review |
| Supabase outer AES-256-GCM cloud encryption | Core codec implemented and CI-tested | Key enrollment + lifecycle tests on both platforms |
| Supabase owner-scoped RLS schema | Migration committed, **not deployed** | Dedicated project, org/cost approval, adversarial two-account SQL/API tests |
| Supabase authenticated REST transfer | Windows/.NET core transport with synthetic tests | Secure Auth login, protected refresh tokens, merge orchestration, Android implementation |
| TOTP | RFC 6238 engine with test vectors, **not wired to vault** | Secret field format, encrypted persistence, QR provisioning, clock drift and backup tests |
| Phishing-resistant Autofill | Exact HTTPS-host matching + generic near-match/IDN warnings, form action origin checks, and Chrome document ID checks implemented in preview | Threat model malicious page JavaScript, compromised browser, cross-origin AJAX, iframe/re-auth, and user spoofing; independent audit |
| Smart Save & credential updates | User-driven Generate, Save and Update Existing with explicit Windows review implemented | Automated login/signup success detection and safe change-password form handling are not yet implemented |
| Windows Hello | Not implemented | User verification, Windows protected-key design, recovery fallback, tested hardware |
| Passkeys/WebAuthn | Not implemented | Browser protocol integration, RP ID isolation, device/private-key secure storage, recovery |
| Import from Chrome/Bitwarden | Windows 1.5 preview: bounded local CSV parser, duplicate/unsafe row filtering, preview, atomic encrypted import | More providers/formats and Android direct import need tests; Bitwarden records with TOTP/custom fields are refused rather than silently lost |
| Publisher-signed Windows releases | Not implemented (no publisher signing certificate) | Real certificate, Authenticode verification, reproducible build and signed update manifest |
| Auto updates | Not implemented | Signed manifest verification, rollback safeguards, transparent consent and install recovery |
| Device approvals/revocation | Not implemented for cloud | Per-device cryptographic enroll, revoke Supabase sessions, cloud-key rekey and offline snapshot warnings |
| Independent security audit | Not performed | External code review + threat modeling + remediation + regression tests |

## Cloud sync launch gates

1. Ask user to choose an organization for a NEW Supabase project. Obtain project pricing and explicit cost confirmation. Do not reuse unrelated projects.
2. Apply owner-isolated migration; audit RLS with independent users and token expiration/revocation.
3. Implement Auth PKCE/email OTP, secure token persistence (Windows DPAPI + Android Keystore), optional MFA and sign-out.
4. Transfer user-controlled random recovery key via verified device pairing. No cloud copy of that key.
5. Seal *complete* encrypted vault with outer AES-256-GCM. Upload/download by distinct per-device slots; verify every snapshot before merge.
6. Merge using revision vectors with explicit conflict notice, key-epoch change resolution, stable sync cursors and replay protection.
7. Add cloud toggle/status with cloud OFF by default, plus local Wi-Fi transfer kept independent. Test offline, failures, lost keys, lost device and cloud account takeover.
8. Conduct external security audit and protect production binaries/signing.

This roadmap describes work remaining, not the presence of hidden features in published apps.
