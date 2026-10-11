# Windows installer and smaller updates

Windows 1.11.0 introduces Velopack 1.2.161, pinned in both the app SDK and local `vpk` tool manifest. The installer bundles .NET and the updater. Users do not install a .NET SDK, Velopack CLI or GitHub CLI.

Download `Lockvyn-win-Setup.exe` from the [published Windows release](https://github.com/R3Dzf/BoshaVault/releases/tag/windows-v1.11.0) and run it. CI also retains the `Lockvyn-Windows-Setup-x64` artifact. Exit the previous app through its tray menu first. The installed program lives in `%LOCALAPPDATA%\Lockvyn\current`; vaults, profiles and approved browser manifests stay in the existing `%LOCALAPPDATA%\BoshaVault` folder. Existing encrypted vaults need no format conversion. An existing startup entry or approved native-host manifest is repointed after install/update without adding browser origins or enabling startup.

Use **Check for updates** from the locked screen or Settings, then **Download update** and **Restart and update**. Downloading is explicit. Applying locks the vault and exits normally before replacement; startup does not silently apply a queued download. Original ZIP copies can still be used, but require this one-time Setup installation to enter the updater layout.

The app reads stable published releases from `https://github.com/R3Dzf/BoshaVault`. The first release has a full installer and full package. Future releases download the previous published package before packing, creating a delta automatically. The SDK uses a valid local base and smaller deltas; a missing base, damaged delta, oversized delta chain or other unsuitable base falls back to the verified full package. File checksums detect damaged downloads. They are not an Authenticode publisher signature; Windows files remain unsigned until a publisher certificate is configured.

If setting up browser autofill after installation, load `%LOCALAPPDATA%\Lockvyn\current\browser-extension` and run the adjacent `Install-BrowserAutofill.ps1`. The existing approved native-host registration is repaired when moving an older ZIP installation into the installer layout. Chrome/Edge may require reloading an unpacked extension if its old source folder has moved.

## GitHub release pipeline

1. Increase the three-part Windows `Version`, upload the source and let **Build Lockvyn** finish successfully.
2. Create the branch `release/windows-v<Version>` (or tag `windows-v<Version>`) at that exact tested commit. **Release Lockvyn Windows** refuses a different version ref or a source commit without successful acceptance. A release branch lets the workflow create the final tag together with the release.
3. The workflow downloads the latest published Windows baseline, packs the release and publishes Setup, full/delta packages, `releases.win.json`, portable ZIP and SHA-256 sums to the same GitHub release. First publication has no delta baseline.

The release workflow also supports manual dispatch once present on the default branch. Its default creates a draft; selecting `publish` publishes it. A public updater cannot see drafts. Windows tags are separate from `v*` Android release tags, so Windows packaging does not require an Android signing key. The short-lived GitHub Actions token is scoped to the release job; no GitHub token is distributed in the app.

After upload, `Publish-WindowsChecksums.ps1` reads the actual GitHub assets, verifies each file against GitHub's digest and size, checks the package against the public update feed, and publishes `SHA256SUMS.txt`. Internal Velopack build manifests are excluded. The separate **Verify Windows release checksums** workflow can repeat this verification for an existing release without rebuilding or replacing the app; use `maintenance/windows-checksums-v<Version>` at a source commit with successful Build Lockvyn acceptance.

CI separately packs synthetic versions 9.0.0 and 9.0.1 in a disposable runner directory. It measures delta/full sizes, reconstructs every file and compares SHA-256 hashes, tests missing-base and damaged-delta fallback, rejects damaged full packages, checks cancellation and version ordering, then performs a real silent installation and delta apply. It checks the encrypted vault byte hash, reopens it and verifies approved browser origins. These synthetic packages are never release assets.
