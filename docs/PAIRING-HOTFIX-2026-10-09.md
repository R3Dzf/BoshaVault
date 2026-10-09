# BoshaVault private Wi-Fi pairing hotfix (source-only)

## Why this hotfix exists

The phone reports that a connection was closed, even after the Windows private IPv4 was changed. Changing an IP cannot fix a TLS handshake or server exception. The original Windows listener handled only a limited subset of exceptions inside each accepted connection. Certain Schannel, socket, or crypto failures could escape that handler, end the 3-minute listener and leave Android with a generic `Connection closed` message. This is a **code review finding**, not confirmation of which exception happened on the user's machine.

## Code changes

- Server catches and categorizes TLS, network and transfer failures **per connection**, rather than silently ending the listener. It remains private-IPv4-only, uses TLS 1.2/1.3, a per-session cert pin, 256-bit temporary pairing token, strict request checks, a three-minute lifetime and up to 16 accepted clients.
- Handles network-interrupted GETs: permits at most three authenticated downloads before the one merged upload, all bounded to the single three-minute session. Does not automatically retry encrypted uploads.
- Windows records timestamps, stages, exception classes and HRESULT codes only in `%LOCALAPPDATA%\BoshaVault\transfer.log` (rotated at ~256 KB). No vault entries, plaintext, master passphrases, tokens, QR strings, IPs, port numbers or certificate pins are logged.
- Android source now categorizes connection, TLS, timeout and premature-EOF errors so the message says **at which phase** it disconnected. **The existing signed 1.1 APK was NOT rebuilt**; that change requires a properly signed Android build using the original signing key to update existing installations safely.
- The existing Windows `X` -> lock and move to tray, explicit Exit and Windows startup behavior is unchanged.

## How to try on the same Windows machine

1. Export a separate encrypted backup from BoshaVault. Do not delete `%LOCALAPPDATA%\BoshaVault` or reinstall Android.
2. Extract this **source** archive, run `Build-Windows-Hotfix.ps1` in PowerShell (requires **.NET 10 SDK**, its package restore access and the tests to pass). The script also runs an actual *local* TLS handshake using the generated pairing certificate and Windows Schannel, then refuses to publish if it fails.
3. Choose **Exit BoshaVault** from its tray context menu; replace the old Windows application binaries with the resulting `BoshaVault-Windows-PairingFix.zip` contents in the existing program directory. Keep the vault data where it is.
4. Restart the app; unlock, choose `Devices & backup` > `Start a private transfer`, confirm the laptop's Wi-Fi IPv4, keep that dialog open, and scan this **new** QR on the same private Wi-Fi with the existing phone app. Do not use a previous pairing code.
5. If it still fails, run `Diagnose-BoshaVault.ps1` **while the QR is visible**. Supply its output plus the final ~25 log lines; never share a pairing QR/token, master password or vault file.
6. If the firewall prompts, allow BoshaVault on a **private network** only. Avoid opening broad inbound ports, turning off Windows Defender or disabling the firewall.

## Limits of verification

Java client syntax compiles with Java 17 against a minimal API stub. This does **not** test Android APIs, real TLS or Windows Schannel. The code cannot be natively built in the current environment because .NET 10 SDK and Android SDK/signing key are not installed and package networking is unavailable. Actual Windows/Android pairing and the tray on a Windows machine remain **unverified**. The hotfix therefore improves retry robustness and makes the remaining failure diagnosable; it is not a claim that real-device pairing is now working.

## Security reminder

Treat this preview as experimental. Use fake credentials first. Keep encrypted backups and do not disable antivirus. The Android APK must not be uninstalled merely to install a differently signed debug build.
