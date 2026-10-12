# Lockvyn Windows 1.20.0 — Optional Passkey suggestion control

## New
- Lockvyn Autofill **0.8.0** adds **Hide automatic Passkey suggestions** in the extension popup (disabled by default).
- The new control tries to suppress a website's **conditional WebAuthn passkey prompts**, which often cause the Chrome/Windows Hello passkey dropdown, while leaving explicit passkey logins and creation available.

## Improvements
- Switch on/off per browser profile with no changes to your Windows Hello keys, Chrome passwords, stored Passkeys or vault.
- On supported tabs, apply the switch without a forced page reload. Other open tabs may require a manual refresh.

## Fixes & safety
- Restricted to HTTPS main documents and opt-in, with no interception of credentials or password data.
- Reversible; disabling restores Lockvyn's own patch.
- JavaScript regression tests for passkey creation, explicit sign-in, opt-in script registration and restoring native browser behavior.

**Limitations:** Some Chrome or Windows native suggestions may still show independently. No extension can guarantee hiding every browser-owned menu. Some websites may require this option to remain off. This is a UI workaround and is not a security boundary or substitute for genuine browser settings. Test manually with non-sensitive accounts first.
