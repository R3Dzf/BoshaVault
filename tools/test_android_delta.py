#!/usr/bin/env python3
"""Build synthetic APK-like byte streams and test delta in both Python and Java."""
from pathlib import Path
import hashlib
import os
import random
import subprocess
import tempfile
from android_delta import create, reconstruct


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    rng = random.Random(31124)
    # Changed small ZIP-like slices plus appending metadata to mimic signed APKs.
    old = rng.randbytes(1200000) + b"A" * 120000 + rng.randbytes(500000)
    new = old[:650000] + b"new-dex-and-signature" * 3900 + old[660000:1300000] + b"app-update" + old[1300000:]
    with tempfile.TemporaryDirectory() as directory:
        d=Path(directory)
        base, target, patch, reconstructed, java_out = [d/name for name in
            ("base.apk", "target.apk", "difference.lvdelta.gz", "py.apk", "java.apk")]
        base.write_bytes(old)
        target.write_bytes(new)
        size=create(base,target,patch)
        if size>=len(new)*0.8:raise AssertionError("Fixture unexpectedly inefficient.")
        reconstruct(base,patch,reconstructed)
        if reconstructed.read_bytes()!=new:raise AssertionError("Python patch roundtrip failed.")
        sources=["android/app/src/main/java/com/bosha/vault/AndroidDelta.java","tools/AndroidDeltaVerifier.java"]
        subprocess.run(["javac","-d",str(d),*sources],check=True)
        call=["java","-cp",str(d),"com.bosha.vault.AndroidDeltaVerifier",
            str(base),str(patch),str(java_out),sha(old),sha(new),str(len(new))]
        subprocess.run(call,check=True)
        if java_out.read_bytes()!=new:raise AssertionError("Java patch roundtrip failed.")
        original=base.read_bytes()
        base.write_bytes(b"x"+original[1:])
        failed=subprocess.run(call,capture_output=True)
        if failed.returncode==0:raise AssertionError("Tampered old APK was accepted.")
        base.write_bytes(original)
        data=patch.read_bytes()
        patch.write_bytes(data[:-13])
        failed=subprocess.run(call,capture_output=True)
        if failed.returncode==0:raise AssertionError("Truncated delta was accepted.")
        print(f"PASS: Python and Java APK deltas identical; original={len(old)}, target={len(new)}, patch={size}; corruption rejected.")


if __name__=="__main__":
    main()
