# Lockvyn brand and compatibility

The product is **Lockvyn**, Arabic **لوكڤين**. Windows is version **1.10.0**, Android **1.6.0**, and the Chromium extension **0.6.0**. Application titles, executable names, launcher labels and download artifacts use Lockvyn without a Preview suffix. The existing GitHub repository name stays unchanged.

The original icon combines a white shield and purple keyhole on a rounded purple square (`#7762DF`). Its master vector is `design/lockvyn-mark.svg`. Windows uses the multi-resolution ICO for its executable/tray and PNG for WPF. Android uses matching vector geometry in `ic_vault.xml`; Chromium has 16/32/48/128px PNGs. `tools/render-brand-icon.py` reproduces the raster assets with Pillow from the original geometry.

Compatibility-sensitive identifiers retain BoshaVault: authenticated file/envelope names and AAD, pairing-code formats, `.boshavault` extension, Windows data directory/startup key/single-instance channel, Android Keystore aliases and native messaging host `com.boshavault.desktop`. Renaming them would break existing encrypted files or registrations. Installers prefer the new executable names and accept old names as a fallback.

The recommended new Android artifact uses `com.lockvyn.app` with separate data, so it installs alongside earlier apps. It requires encrypted import/transfer of an existing vault. The legacy-package artifact keeps `com.bosha.vault`; certificate matching remains necessary for in-place updates.
