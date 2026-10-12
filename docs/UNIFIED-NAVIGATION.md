# Lockvyn navigation and safe advanced workflows

The Windows 1.15.0 / Android 1.12.0 release aims for **four primary destinations**:

| Primary destination | Windows | Android |
|---|---|---|
| My Vault | login list + collapsible favorites/review/trash | vault list + small built-in filters |
| Sync & Devices | cloud status, Cloud Sync, Add device, optional offline transfer | cloud status, Sync Now entry, secure QR join, offline transfer behind Advanced |
| Backup & Restore | Export encrypted backup, Import/merge encrypted backup | encrypted export and safe import/merge |
| Settings | Autofill, startup, vault security, updates | Autofill, biometrics, security, updates |

Security-critical fallback paths must never be deleted purely for simplicity: local Wi-Fi transfer, firewall troubleshooting, temporary QR, old recovery key, account MFA, cloud disconnect and local token forget remain available under appropriately labelled Advanced sections. The normal flow should **never expose or copy a permanent recovery key by default**.

Version badges are read from the **running application**. Windows release notes and Velopack update feed distinguish the installed version from the available target. Before downloading, show the actual full/delta package sizes from the feed. Velopack progress is a combined download/reconstruction/verifying percentage and is not an independently measured byte counter. Changing the UI must not change vault data formats, encryption keys, cloud account RLS, signing keys or app package identity.

Release qualification: Windows CI, cloud interoperability checks, native Android tests and Android emulator smoke tests must pass on the same exact source revision. A user-device test must still confirm secure QR pairing, lifecycle navigation, in-place installation and background sync on the real TECNO phone and Windows laptop.
