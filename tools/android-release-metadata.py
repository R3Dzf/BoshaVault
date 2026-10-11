#!/usr/bin/env python3
"""Generate a release feed only from a verified, permanently signed canonical APK."""
import argparse,hashlib,json,pathlib,re,subprocess,urllib.request,os
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
 target=apk.parent/'android-update.json';target.write_text(json.dumps(feed,indent=2)+'\n')
 sums=[]
 for file in [apk,target]:sums.append(hashlib.sha256(file.read_bytes()).hexdigest()+'  '+file.name)
 (apk.parent/'SHA256SUMS-Android.txt').write_text('\n'.join(sums)+'\n')
 print('PASS: APK signature, package, version and signing continuity verified; release metadata generated.')
if __name__=='__main__':main()
