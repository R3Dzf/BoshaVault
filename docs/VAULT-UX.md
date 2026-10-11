# Lockvyn vault UX — Windows 1.10 / Android 1.6

## Use the new controls

- **Options**, beside the vault heading, opens Add login, Generate password, Folders, Devices & encrypted backup and Settings. Android keeps the heading, search, sections and folder selector above a recycled scrolling list. Windows keeps its sidebar and adds the same Options entry point.
- Use the folder dropdown above the list to narrow **All**, **Favorites**, **Review** or **Trash** independently. Add/edit a login to assign its folder. Windows lets you type a new folder name into the editable dropdown; Android offers **+ New folder**. **Options → Folders** can also create an empty persistent custom folder.
- In **Trash**, open a login and choose **Delete permanently**. Confirmation removes all credential fields from the current vault and blocks restoration of that identity by an old or concurrent merged snapshot. Exported backups and devices that have not synchronized can still hold old encrypted copies.
- Open an entry in **Review** and choose **I reviewed this login**. After explicit confirmation, its current warnings leave Review. Password or risk changes bring it back. Acceptance does not make a weak/reused password stronger.
- **Generate password** initially uses length **24**. Choose 8–128 characters, uppercase/lowercase/digits/symbols, permitted symbols, minimum digit/symbol counts and whether to exclude confusing characters. Every enabled group is guaranteed to appear. Invalid or impossible rules are rejected. **Save these rules as my default** persists only these nonsecret rules on the current device. Add/edit can use the generated value directly; copying remains explicit.

## Installation and existing vaults

Export an encrypted backup before changing versions. Update both devices before saving and exchanging schema 2. These builds read older vaults; old builds do not read schema 2. Android **Lockvyn** (`com.lockvyn.app`) has a different package/storage from the production app and earlier Test/Preview packages. Install it alongside the old app, then import an encrypted backup through the lock screen or receive the existing Windows vault over Wi-Fi. Keep the original app and backup while checking the imported vault. Production APK upgrades still require the original stable signing certificate.

## Signup and project priorities

Friends use their own Lockvyn cloud account created inside the app. They need no Supabase dashboard account. Local vault use needs no cloud signup. Public email confirmation still requires the app owner to configure a real SMTP sender; the connected Resend account has no verified sender domain. The UI provides signup/resend and useful delivery error messages, but this infrastructure task has not been completed.

| Priority | Work | Status |
|---|---|---|
| Current | Keyboard, reachable options, efficient list, folder classification, permanent Trash deletion, review acknowledgement, generator rules | Implemented; automated validation recorded in VALIDATION.md |
| Before distributing cloud signup | Verified transactional sender + Auth configuration + real authorized signup/confirmation tests | Waiting for owned sender domain or existing SMTP details |
| Next onboarding improvement | Recover cloud-account password inside app; initialize a new device from cloud using the recovery code | Planned; current second-device setup uses Wi-Fi/encrypted backup first |
| Before stable Android distribution | Permanent signing certificate and upgrade acceptance tests | Requires the repository's release signing setup; CI Lockvyn package has separate storage |
| Later | Opt-in automatic sync, device/session management and account recovery | Needs lifecycle, key rotation and account recovery design |

Future cloud account recovery must remain separate from the vault master passphrase. A forgotten vault passphrase cannot be reset through email. An independent security review remains a separate release milestone.
