#!/usr/bin/env python3
"""Put official Windows and Android binaries in one public GitHub release.

Legacy platform releases and update feeds remain intact for existing clients.
All uploaded files are verified against the source release's SHA-256 digests.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import xml.etree.ElementTree as ET

REPOSITORY = "R3Dzf/BoshaVault"
ROOT = Path(__file__).resolve().parents[1]
ANDROID_ASSETS = re.compile(
    r"(?:Lockvyn-Android\.apk|android-update\.json|SHA256SUMS-Android\.txt|"
    r"Lockvyn-Android-from-[0-9]+\.lvdelta\.gz)"
)


def gh(*args: str) -> str:
    result = subprocess.run(("gh", *args), text=True, capture_output=True, check=False)
    if result.returncode:
        raise RuntimeError("GitHub operation failed: " + " ".join(args[:3]) + ": " + result.stderr[-750:])
    return result.stdout


def get_release(tag: str) -> dict:
    release = json.loads(gh("api", "repos/" + REPOSITORY + "/releases/tags/" + tag))
    if release["tag_name"] != tag or release["draft"] or release["prerelease"]:
        raise RuntimeError("A stable published source release is required: " + tag)
    return release


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        h = hashlib.sha256()
        while piece := stream.read(1024 * 1024):
            h.update(piece)
        return h.hexdigest()


def verify(path: Path, asset: dict) -> None:
    expected = asset.get("digest", "")
    if not re.fullmatch(r"sha256:[a-f0-9]{64}", expected):
        raise RuntimeError("Missing signed source-release digest for " + path.name)
    if not path.is_file() or path.stat().st_size != asset["size"] or digest(path) != expected[7:]:
        raise RuntimeError("Source-release checksum mismatch for " + path.name)


def main() -> None:
    version = ET.parse(ROOT / "src/BoshaVault.Windows/BoshaVault.Windows.csproj").getroot()
    win_version = version.findtext(".//Version")
    android = json.loads((ROOT / "android/version.json").read_text(encoding="utf-8"))
    android_version = android["versionName"]
    if not isinstance(win_version, str) or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", win_version):
        raise RuntimeError("Invalid Windows release version.")
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", android_version):
        raise RuntimeError("Invalid Android release version.")
    windows_tag, android_tag = "windows-v" + win_version, "android-v" + android_version
    if not (ROOT / "docs/releases" / ("Lockvyn-" + win_version + ".md")).exists():
        raise RuntimeError("Missing Windows release notes")
    if not (ROOT / "docs/releases" / ("Lockvyn-Android-" + android_version + ".md")).exists():
        raise RuntimeError("Missing Android release notes")

    windows_release = get_release(windows_tag)
    android_release = get_release(android_tag)
    windows_assets = {a["name"]: a for a in windows_release["assets"]}
    android_assets = {a["name"]: a for a in android_release["assets"]}
    if not {"Lockvyn-win-Setup.exe", "Lockvyn-" + win_version + "-full.nupkg", "releases.win.json"}.issubset(windows_assets):
        raise RuntimeError("Windows release is incomplete")
    if not {"Lockvyn-Android.apk", "android-update.json", "SHA256SUMS-Android.txt"}.issubset(android_assets):
        raise RuntimeError("Android release is incomplete")

    with tempfile.TemporaryDirectory(prefix="lockvyn-bundle-") as directory:
        folder = Path(directory)
        android_files = sorted(name for name in android_assets if ANDROID_ASSETS.fullmatch(name))
        for name in android_files:
            gh("release", "download", android_tag, "--repo", REPOSITORY, "--pattern", name, "--dir", str(folder))
            verify(folder / name, android_assets[name])
        metadata = json.loads((folder / "android-update.json").read_text())
        if metadata["tag"] != android_tag or metadata["versionName"] != android_version:
            raise RuntimeError("Unexpected Android release metadata tag")
        if digest(folder / "Lockvyn-Android.apk") != metadata["sha256"]:
            raise RuntimeError("Android APK does not match the updater feed")
        checksums = {}
        for line in (folder / "SHA256SUMS-Android.txt").read_text().splitlines():
            parts = line.split()
            if len(parts) != 2 or parts[1] in checksums or not re.fullmatch("[a-f0-9]{64}", parts[0]):
                raise RuntimeError("Invalid Android checksums file")
            checksums[parts[1]] = parts[0]
        for name in android_files:
            if name != "SHA256SUMS-Android.txt" and checksums.get(name) != digest(folder / name):
                raise RuntimeError("Android checksums mismatch: " + name)

        for name in android_files:
            if name in windows_assets:
                if windows_assets[name]["digest"] != android_assets[name]["digest"]:
                    raise RuntimeError("Combined release contains a conflicting asset: " + name)
                print("Already bundled:", name)
                continue
            gh("release", "upload", windows_tag, str(folder / name), "--repo", REPOSITORY)
            print("Bundled:", name)

        # Keep Android's original tag for the current phone updater, which
        # recognizes android-v* only. The Windows release is the single
        # convenient download page for both platforms.
        combined_notes = (
            "# Lockvyn — Windows " + win_version + " + Android " + android_version + "\n\n"
            "Both apps are available from **this release page**.\n\n"
            "- **Windows:** `Lockvyn-win-Setup.exe`\n"
            "- **Android:** `Lockvyn-Android.apk` (official permanent signing certificate)\n"
            "- Windows delta and Android delta assets remain available for in-app updates.\n"
            "- For compatibility, the Android updater still uses its existing "
            "[Android update feed](https://github.com/" + REPOSITORY
            + "/releases/tag/" + android_tag + ").\n\n"
            "## Windows changes\n\n"
            + (ROOT / "docs/releases" / ("Lockvyn-" + win_version + ".md")).read_text()
            + "\n\n## Android changes\n\n"
            + (ROOT / "docs/releases" / ("Lockvyn-Android-" + android_version + ".md")).read_text()
        )
        note_file = folder / "combined-notes.md"
        note_file.write_text(combined_notes, encoding="utf-8")
        gh("release", "edit", windows_tag, "--repo", REPOSITORY,
           "--title", "Lockvyn " + win_version + " — Windows + Android",
           "--notes-file", str(note_file))
        after = get_release(windows_tag)
        after_assets = {asset["name"]: asset for asset in after["assets"]}
        for name in android_files:
            if name not in after_assets or after_assets[name]["digest"] != android_assets[name]["digest"]:
                raise RuntimeError("Bundled release failed final verification: " + name)
        print("PASS: Unified Windows + Android release: https://github.com/"
              + REPOSITORY + "/releases/tag/" + windows_tag)


if __name__ == "__main__":
    main()
