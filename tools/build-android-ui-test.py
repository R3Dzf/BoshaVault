#!/usr/bin/env python3
"""Build emulator-only instrumentation without adding test code to shipped APKs."""
from pathlib import Path
import argparse, hashlib, os, subprocess, tempfile, urllib.request, zipfile

ROOT = Path(__file__).resolve().parents[1]
def run(*args):
    subprocess.run([str(x) for x in args], check=True)
def main():
    p=argparse.ArgumentParser();p.add_argument('--sdk',required=True);p.add_argument('--tools',required=True);p.add_argument('--keystore',required=True);p.add_argument('--update-apk');p.add_argument('--wrong-signer-apk');p.add_argument('--lan-pairing');a=p.parse_args()
    sdk=Path(a.sdk);tools=Path(a.tools);deps=ROOT/'build-deps';deps.mkdir(exist_ok=True)
    ecj=deps/'ecj.jar'
    if not ecj.exists():urllib.request.urlretrieve('https://repo.maven.apache.org/maven2/org/eclipse/jdt/ecj/3.42.0/ecj-3.42.0.jar',ecj)
    if hashlib.sha256(ecj.read_bytes()).hexdigest()!='29f6d3918ee02db4400c103bc25dd90a22491c3a395867d9393070cb96a7dd29':raise SystemExit('Compiler checksum mismatch')
    work=Path(tempfile.mkdtemp(prefix='ui-test-',dir=deps));classes=work/'classes';classes.mkdir();android=sdk/'android.jar'
    manifest=work/'AndroidManifest.xml';manifest.write_text('<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.bosha.vault.uismoke"><uses-sdk android:minSdkVersion="28" android:targetSdkVersion="35"/><application android:label="Lockvyn UI tests"/><instrumentation android:name="com.bosha.vault.tests.UiSmoke" android:targetPackage="com.lockvyn.app"/><instrumentation android:name="com.bosha.vault.tests.UpdateSmoke" android:targetPackage="com.lockvyn.app"/><instrumentation android:name="com.bosha.vault.tests.LanSmoke" android:targetPackage="com.lockvyn.app"/></manifest>',encoding='utf-8')
    unsigned=work/'unsigned.apk';run(tools/'aapt2','link','-o',unsigned,'--manifest',manifest,'-I',android,'--min-sdk-version','28','--target-sdk-version','35')
    run('java','-jar',ecj,'-1.8','-warn:none','-bootclasspath',str(android)+os.pathsep+str(tools/'core-lambda-stubs.jar'),'-d',classes,*sorted((ROOT/'tools/android').glob('*.java')))
    jar=work/'test.jar';run('jar','cf',jar,'-C',classes,'.');dex=work/'dex';dex.mkdir();run(tools/'d8','--lib',android,'--min-api','28','--output',dex,jar)
    with zipfile.ZipFile(unsigned,'a',zipfile.ZIP_DEFLATED) as z:
        for path in dex.glob('*.dex'):z.write(path,path.name)
        for field,name in [('update_apk','update.apk'),('wrong_signer_apk','wrong-signer.apk')]:
            if getattr(a,field):z.write(getattr(a,field),'assets/'+name)
        if a.lan_pairing:z.write(a.lan_pairing,'assets/lan-pairing.txt')
    aligned=work/'aligned.apk';run(tools/'zipalign','-f','4',unsigned,aligned);output=ROOT/'dist/android/Lockvyn-UI-Test.apk';output.parent.mkdir(parents=True,exist_ok=True)
    run(tools/'apksigner','sign','--ks',a.keystore,'--ks-key-alias','boshavault','--ks-pass','env:BOSHAVAULT_KEYSTORE_PASSWORD','--key-pass','env:BOSHAVAULT_KEYSTORE_PASSWORD','--out',output,aligned)
    run(tools/'apksigner','verify','--verbose',output)
if __name__=='__main__':main()
