#!/usr/bin/env python3
"""Build without Gradle. Requires Java 17+, Android platform 35 and Build Tools 35."""
from pathlib import Path
import argparse, hashlib, os, shutil, subprocess, tempfile, urllib.request, zipfile

ROOT=Path(__file__).resolve().parents[1]
DEPS=ROOT/'build-deps'
PINS={
 'bcprov.jar':('https://repo.maven.apache.org/maven2/org/bouncycastle/bcprov-jdk18on/1.86/bcprov-jdk18on-1.86.jar','2af190b300cbb0b35e248ccf5f4a06b6072030aeb3da7a98ec73abe5b4cb371f'),
 'zxing-core.jar':('https://repo.maven.apache.org/maven2/com/google/zxing/core/3.5.4/core-3.5.4.jar','71de5d89341b5fcf5dd89da7f44e84d825d0e084cdf3ec77c9abe26b0f0ceb13'),
 'ecj.jar':('https://repo.maven.apache.org/maven2/org/eclipse/jdt/ecj/3.42.0/ecj-3.42.0.jar','29f6d3918ee02db4400c103bc25dd90a22491c3a395867d9393070cb96a7dd29')
}
def run(*args):subprocess.run([str(x) for x in args],check=True)
def main():
 p=argparse.ArgumentParser();p.add_argument('--sdk',required=True);p.add_argument('--tools',required=True);p.add_argument('--keystore',required=True);p.add_argument('--alias',default='boshavault');p.add_argument('--isolated-test-app',action='store_true',help='Build a separate demo app with isolated local storage');a=p.parse_args();sdk=Path(a.sdk);tools=Path(a.tools)
 if not os.environ.get('BOSHAVAULT_KEYSTORE_PASSWORD'):raise SystemExit('Set BOSHAVAULT_KEYSTORE_PASSWORD to your signing password. Do not check keys into source control.')
 DEPS.mkdir(exist_ok=True)
 for name,(url,pin) in PINS.items():
  dest=DEPS/name
  if not dest.exists():urllib.request.urlretrieve(url,dest)
  if pin and hashlib.sha256(dest.read_bytes()).hexdigest()!=pin:raise SystemExit('Dependency checksum mismatch: '+name)
 work=Path(tempfile.mkdtemp(prefix='apk-build-',dir=DEPS))
 classes=work/'classes';classes.mkdir(parents=True);res=work/'res.zip';compiled=work/'unsigned.apk';app=ROOT/'android/app/src/main';android=sdk/'android.jar'
 manifest=app/'AndroidManifest.xml'
 if a.isolated_test_app:
  # Android package must differ so the trial APK can never overwrite the user's vault.
  # Java class names stay unchanged, so explicitly qualify manifest component names.
  xml=manifest.read_text(encoding='utf-8').replace('package="com.bosha.vault"','package="com.bosha.vault.test"').replace('android:label="BoshaVault"','android:label="BoshaVault Test"').replace('android:name=".','android:name="com.bosha.vault.')
  manifest=work/'AndroidManifest.xml'
  manifest.write_text(xml,encoding='utf-8')
 run(tools/'aapt2','compile','--dir',app/'res','-o',res)
 run(tools/'aapt2','link','-o',compiled,'--manifest',manifest,'-I',android,'--min-sdk-version','28','--target-sdk-version','35','--version-code','5','--version-name','1.3.1-biometric-compat',res)
 classpath=os.pathsep.join(str(DEPS/name) for name in ('bcprov.jar','zxing-core.jar'))
 run('java','-jar',DEPS/'ecj.jar','-1.8','-warn:none','-bootclasspath',str(android)+os.pathsep+str(tools/'core-lambda-stubs.jar'),'-cp',classpath,'-d',classes,*sorted((app/'java').rglob('*.java')))
 jar=work/'app.jar'
 with zipfile.ZipFile(jar,'w')as z:
  for f in classes.rglob('*.class'):z.write(f,f.relative_to(classes))
 dex=work/'dex';dex.mkdir();d8=tools/('d8.bat' if os.name=='nt' else 'd8')
 run(d8,'--release','--min-api','28','--lib',android,'--output',dex,jar,DEPS/'bcprov.jar',DEPS/'zxing-core.jar')
 with zipfile.ZipFile(compiled,'a')as z:
  for f in dex.glob('*.dex'):z.write(f,f.name)
 aligned=work/'aligned.apk';run(tools/'zipalign','-f','4',compiled,aligned);out=ROOT/('dist/BoshaVault-Android-Test.apk' if a.isolated_test_app else 'dist/BoshaVault-Android.apk');out.parent.mkdir(exist_ok=True)
 signer=tools/('apksigner.bat' if os.name=='nt' else 'apksigner')
 run(signer,'sign','--ks',a.keystore,'--ks-key-alias',a.alias,'--ks-pass','env:BOSHAVAULT_KEYSTORE_PASSWORD','--key-pass','env:BOSHAVAULT_KEYSTORE_PASSWORD','--out',out,aligned)
 run(signer,'verify','--verbose',out);shutil.rmtree(work);print(out)
if __name__=='__main__':main()
