#!/usr/bin/env python3
"""Small zero-dependency design contract for cross-platform Calm Pro UI.

Compile/runtime smoke testing still takes place in Windows WPF and Android
emulator CI jobs. This test detects common regressions before either build.
"""
from pathlib import Path
from xml.etree import ElementTree as ET

root = Path(__file__).resolve().parent.parent
android = (root / "android/app/src/main/java/com/bosha/vault/Ui.java").read_text()
main = (root / "android/app/src/main/java/com/bosha/vault/MainActivity.java").read_text()
app = root / "src/BoshaVault.Windows/App.xaml"
window = root / "src/BoshaVault.Windows/MainWindow.xaml"

ET.parse(app)
ET.parse(window)
icons = ("vault", "sync", "backup", "settings")
for name in icons:
    vector = root / f"android/app/src/main/res/drawable/ic_nav_{name}.xml"
    xml = ET.parse(vector).getroot()
    assert xml.tag == "vector" and len(xml) > 0, f"Missing vector path: {vector}"
    assert f'Ui.drawableId(this,"ic_nav_{name}")' in main
assert "setStateListAnimator(null)" in android, "Do not reintroduce native Button elevation animator"
assert "setElevation(0f)" in android and "setTranslationZ(0f)" in android
assert "RippleDrawable" in android and "bottomNavigation" in android
assert "new String[]{\"Vault\",\"Sync\",\"Backup\",\"Settings\"}" in main
for action in ("this::showVault", "this::syncHub", "this::backupHub", "this::settings"):
    assert action in main
assert "setMinHeight(dp(c,50))" in android, "Minimum 48dp button target required"
assert "FLAG_SECURE" in android, "Android private screen protection must stay enabled"

# Autofill must really disable the owned service (not reopen the enable prompt).
assert "manager.disableAutofillServices()" in main, "Disable Autofill must call Android's owned-service API"
assert 'AutofillStatus.isEnabled(this)?"Disable Android Autofill":"Enable Android Autofill"' in main
assert "refreshAutofillSettingsState();" in main, "Return from provider settings must refresh UI state"

wpf = app.read_text()
xaml = window.read_text()
for color in ("#171B2E", "#697185", "#6558D9", "#EFEDFF", "#F7F8FC"):
    assert color in wpf, f"Missing Calm Pro desktop design token {color}"
for nav in ("My Vault", "Sync &amp; Devices", "Backup &amp; Restore", "Settings"):
    assert nav in xaml, f"Missing desktop navigation {nav}"
for name in ("InstalledVersionLabel", "SidebarVersionLabel"):
    assert name in xaml, "Installed version must come from the running assembly"
assert "BUILT FOR PRIVACY" not in xaml, "Decorative-only dashboard card must remain removed"
print("PASS Calm Pro UI, Android Autofill state/disable contracts, WPF XAML and privacy guards")
