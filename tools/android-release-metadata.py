#!/usr/bin/env python3
"""Generate a release feed only from a verified, permanently signed canonical APK."""
import argparse,hashlib,json,pathlib,re,subprocess,urllib.request,os,tempfile
from android_delta import create as create_delta, reconstruct as reconstruct_delta
ROOT=pathlib.Path(__file__).resolve().parents[1]
def main():
 p=argparse.ArgumentParser();p.add_argument('--tools',required=True);p.add_argument('--apk',default='dist/Lockvyn-Android.apk');p.add_argument('--repository',default='R3Dzf/BoshaVault');a=p.parse_args()
 if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+',a.repository):raise SystemExit('Invalid repository.')
 apk=pathlib.Path(a.apk);v=json.loads((ROOT/'android/version.json').read_text());tools=pathlib.Path(a.tools)
 output=subprocess.check_output([str(tools/'apksigner'),'verify','--verbose','--print-certs',str(apk)],text=True)
 signs=re.findall(r'Signer #\d+ certificate SHA-256 digest: ([a-fA-F0-9]{64})',output)
 if len(signs)!=1:raise SystemExit('Exactly one signing certificate is required.')
 cert=signs[0].lower();badging=subprocess.check_output([str(tools/'aapt'),'dump','badging',str(apk)],text=True)
 line=badging.splitlines()[0]
 for field,expected in [('name',v['packageName']),('versionCode',str(v['versionCode'])),('versionName',v['versionName'])]:
  if not re.search(r"\b"+field+"='"+re.escape(expected)+"'",line):raise SystemExit('APK identity differs from source version.')
 if not re.search(r"sdkVersion:'28'",badging):raise SystemExit('Unexpected minimum Android version.')
 headers={'User-Agent':'Lockvyn-Release','Accept':'application/vnd.github+json'}
 if os.environ.get('GH_TOKEN'):headers['Authorization']='Bearer '+os.environ['GH_TOKEN']
 with urllib.request.urlopen(urllib.request.Request('https://api.github.com/repos/'+a.repository+'/releases?per_page=100',headers=headers),timeout=30) as r:releases=json.load(r)
 previous=[]
 for r in releases:
  if r['draft'] or r['prerelease'] or not r['tag_name'].startswith('android-v'):continue
  for asset in r['assets']:
   if asset['name']=='android-update.json':
    # Public metadata only. No credentials are forwarded to a release download redirect.
    with urllib.request.urlopen(urllib.request.Request(asset['browser_download_url'],headers={'User-Agent':'Lockvyn-Release'}),timeout=30) as stream:previous.append(json.load(stream))
 for old in previous:
  if old['packageName']!=v['packageName'] or old['signerSha256'].lower()!=cert:raise SystemExit('Release identity changed. Reuse the existing signing certificate.')
  if int(old['versionCode'])>=v['versionCode']:raise SystemExit('Increase Android versionCode above every published stable release.')
 tag='android-v'+v['versionName'];feed={'schema':1,**v,'tag':tag,'size':apk.stat().st_size,'sha256':hashlib.sha256(apk.read_bytes()).hexdigest(),'signerSha256':cert,'url':'https://github.com/'+a.repository+'/releases/download/'+tag+'/Lockvyn-Android.apk'}
 if not 0<feed['size']<=64*1024*1024:raise SystemExit('Unexpected APK size.')
 # Generate patches from the latest three previously published official APKs.
 # The baseline APK is downloaded anonymously; verify SHA256, certificate and package
 # before using it. Mismatched/re-signed installs will fall back to the full APK.
 feed['deltas']=[]
 for old in sorted(previous,key=lambda x:int(x['versionCode']),reverse=True)[:3]:
  old_tag=old.get('tag','')
  old_code=old.get('versionCode')
  if not isinstance(old_code,int) or old_code<1 or old_code>=v['versionCode'] or not re.fullmatch(r'android-v[0-9]+\.[0-9]+\.[0-9]+',old_tag):continue
  old_url='https://github.com/'+a.repository+'/releases/download/'+old_tag+'/Lockvyn-Android.apk'
  old_size=old.get('size')
  old_sha=old.get('sha256','')
  if old.get('url')!=old_url or not isinstance(old_size,int) or not 0<old_size<=64*1024*1024 or not re.fullmatch(r'[a-f0-9]{64}',old_sha):
   raise SystemExit('Invalid official delta baseline metadata.')
  with tempfile.TemporaryDirectory(prefix='lockvyn-delta-') as folder:
   base=pathlib.Path(folder)/'base.apk'
   try:
    with urllib.request.urlopen(urllib.request.Request(old_url,headers={'User-Agent':'Lockvyn-Delta-Build'}),timeout=35) as stream,base.open('wb') as dest:
     downloaded=0
     while chunk:=stream.read(65536):
      downloaded+=len(chunk)
      if downloaded>old_size:raise ValueError('Oversized previous APK.')
      dest.write(chunk)
    if downloaded!=old_size or hashlib.sha256(base.read_bytes()).hexdigest()!=old_sha:
     raise ValueError('Official APK baseline checksum mismatch.')
    prior_cert_output=subprocess.check_output([str(tools/'apksigner'),'verify','--verbose','--print-certs',str(base)],text=True)
    fingerprints=re.findall(r'Signer #\d+ certificate SHA-256 digest: ([a-fA-F0-9]{64})',prior_cert_output)
    if len(fingerprints)!=1 or fingerprints[0].lower()!=cert:
     raise ValueError('Official APK baseline signing certificate changed.')
    filename=f'Lockvyn-Android-from-{old_code}.lvdelta.gz'
    delta=apk.parent/filename
    patch_bytes=create_delta(base,apk,delta)
    if patch_bytes>=int(apk.stat().st_size*0.85):
     delta.unlink();print(f'SKIP delta from {old_tag}: little or no size savings.');continue
    reconstructed=pathlib.Path(folder)/'reconstructed.apk'
    reconstruct_delta(base,delta,reconstructed)
    if hashlib.sha256(reconstructed.read_bytes()).hexdigest()!=feed['sha256']:
     raise ValueError('APK reconstructed by delta did not match the canonical signed target.')
    feed['deltas'].append({'fromVersionCode':old_code,'fromSha256':old_sha,
      'size':patch_bytes,'sha256':hashlib.sha256(delta.read_bytes()).hexdigest(),
      'url':'https://github.com/'+a.repository+'/releases/download/'+tag+'/'+filename})
    print(f'PASS: Android delta {old_tag} -> {tag}: {patch_bytes}/{apk.stat().st_size} bytes.')
   except (OSError,ValueError,subprocess.CalledProcessError) as error:
    raise SystemExit('Verified Android delta generation failed for '+old_tag+': '+str(error))
 target=apk.parent/'android-update.json';target.write_text(json.dumps(feed,indent=2)+'\n')
 sums=[]
 for file in [apk,target,*sorted(apk.parent.glob('Lockvyn-Android-from-*.lvdelta.gz'))]:sums.append(hashlib.sha256(file.read_bytes()).hexdigest()+'  '+file.name)
 (apk.parent/'SHA256SUMS-Android.txt').write_text('\n'.join(sums)+'\n')
 print('PASS: APK signature, package, version and signing continuity verified; release metadata generated.')
if __name__=='__main__':main()
