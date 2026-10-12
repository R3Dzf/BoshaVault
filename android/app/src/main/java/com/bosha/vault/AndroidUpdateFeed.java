package com.bosha.vault;

import java.net.URI;
import org.json.*;

/** Public release metadata. Installed APK identity remains the trust anchor. */
final class AndroidUpdateFeed {
    static final String REPOSITORY="https://github.com/R3Dzf/BoshaVault";
    static final long MAX_APK=64L*1024*1024;
    final String versionName,sha256,signerSha256,url,tag,packageName;
    final long versionCode,size;
    final int minSdk;
    static final class Delta {
        final long fromVersionCode,size;
        final String fromSha256,sha256,url;
        Delta(JSONObject o,String tag,long targetCode,long fullSize)throws Exception{
            fromVersionCode=integer(o,"fromVersionCode");
            fromSha256=o.getString("fromSha256");sha256=o.getString("sha256");
            size=integer(o,"size");url=o.getString("url");
            if(fromVersionCode<=0||fromVersionCode>=targetCode||
               size<=0||size>=fullSize||size>MAX_APK||
               !fromSha256.matches("[a-fA-F0-9]{64}")||
               !sha256.matches("[a-fA-F0-9]{64}")||
               !url.equals(REPOSITORY+"/releases/download/"+tag+"/Lockvyn-Android-from-"+fromVersionCode+".lvdelta.gz"))
                throw new Exception("Invalid Android delta release metadata.");
        }
    }
    final java.util.List<Delta> deltas=new java.util.ArrayList<>();
    Delta forInstalled(long versionCode,String installedSha256){
        for(Delta delta:deltas)
            if(delta.fromVersionCode==versionCode&&delta.fromSha256.equalsIgnoreCase(installedSha256))
                return delta;
        return null;
    }
    private AndroidUpdateFeed(JSONObject o)throws Exception{
        packageName=o.getString("packageName");versionName=o.getString("versionName");tag=o.getString("tag");sha256=o.getString("sha256");signerSha256=o.getString("signerSha256");url=o.getString("url");versionCode=integer(o,"versionCode");size=integer(o,"size");long sdk=integer(o,"minSdk");if(sdk<28||sdk>100)throw new Exception("Invalid Android minimum version.");minSdk=(int)sdk;
        if(integer(o,"schema")!=1||!packageName.equals("com.lockvyn.app")||!versionName.matches("[0-9]+\\.[0-9]+\\.[0-9]+")||!tag.equals("android-v"+versionName)||versionCode<1||versionCode>Integer.MAX_VALUE||size<1||size>MAX_APK||minSdk<28||minSdk>100)throw new Exception("Invalid Android release metadata.");
        if(!sha256.matches("[a-fA-F0-9]{64}")||!signerSha256.matches("[a-fA-F0-9]{64}"))throw new Exception("Invalid release checksum.");
        if(!url.equals(REPOSITORY+"/releases/download/"+tag+"/Lockvyn-Android.apk"))throw new Exception("Unexpected download source.");
        if(o.has("deltas")){
            JSONArray rows=o.getJSONArray("deltas");
            if(rows.length()>3)throw new Exception("Too many Android delta sources.");
            java.util.HashSet<Long> used=new java.util.HashSet<>();
            for(int i=0;i<rows.length();i++){
                Delta delta=new Delta(rows.getJSONObject(i),tag,versionCode,size);
                if(!used.add(delta.fromVersionCode))throw new Exception("Repeated Android delta baseline.");
                deltas.add(delta);
            }
        }
    }
    static AndroidUpdateFeed parse(String json,String expectedTag)throws Exception{
        if(json.length()>16384)throw new Exception("Release metadata is too large.");
        AndroidUpdateFeed feed=new AndroidUpdateFeed(StrictJson.object(json));
        if(!feed.tag.equals(expectedTag))throw new Exception("Release tag and metadata differ.");return feed;
    }
    void requireUpgrade(String installedPackage,long installedCode,String installedSigner,int sdk)throws Exception{
        if(!packageName.equals(installedPackage))throw new Exception("This older app edition needs an encrypted backup imported into the stable Lockvyn app.");
        if(versionCode<=installedCode)throw new Exception("This update is not newer than the installed app.");
        if(minSdk>sdk)throw new Exception("This update needs a newer Android version.");
        if(!signerSha256.equalsIgnoreCase(installedSigner))throw new Exception("This test edition cannot accept the stable release. Export and verify an encrypted backup before switching editions.");
    }
    static long integer(JSONObject o,String key)throws Exception{Object v=o.get(key);if(!(v instanceof Integer)&&!(v instanceof Long))throw new Exception("Invalid release number: "+key);return ((Number)v).longValue();}
    static boolean allowedRedirect(String address){
        try{URI u=new URI(address);String h=u.getHost();return "https".equals(u.getScheme())&&u.getUserInfo()==null&&(u.getPort()==-1||u.getPort()==443)&&h!=null&&(h.equals("github.com")||h.equals("api.github.com")||h.endsWith(".githubusercontent.com"));}catch(Exception e){return false;}
    }
}
