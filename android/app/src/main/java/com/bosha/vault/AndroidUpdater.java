package com.bosha.vault;

import android.content.*;
import android.content.pm.*;
import android.os.Build;
import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.concurrent.atomic.AtomicBoolean;
import org.json.*;

final class AndroidUpdater {
    interface Progress{void changed(int percent);}
    final AtomicBoolean cancelled=new AtomicBoolean();
    private volatile HttpURLConnection connection;
    void cancel(){cancelled.set(true);HttpURLConnection c=connection;if(c!=null)c.disconnect();}
    private void alive()throws IOException{if(cancelled.get()||Thread.currentThread().isInterrupted())throw new IOException("Download cancelled.");}
    static File apk(Context c){return new File(c.getCacheDir(),"lockvyn-update.apk");}
    static String hex(byte[] bytes){StringBuilder s=new StringBuilder();for(byte b:bytes)s.append(String.format(java.util.Locale.ROOT,"%02x",b&255));return s.toString();}
    static String signer(PackageInfo p)throws Exception{
        if(p==null||p.signingInfo==null||p.signingInfo.hasMultipleSigners())throw new Exception("Unsupported APK signing identity.");
        Signature[] signs=p.signingInfo.getApkContentsSigners();if(signs==null||signs.length!=1)throw new Exception("APK signing identity is missing.");return hex(MessageDigest.getInstance("SHA-256").digest(signs[0].toByteArray()));
    }
    static PackageInfo installed(Context c)throws Exception{return c.getPackageManager().getPackageInfo(c.getPackageName(),PackageManager.GET_SIGNING_CERTIFICATES);}
    private JSONObject releasePolicy()throws Exception{
        String url=CloudClient.URL+"functions/v1/lockvyn-admin/release-policy?platform=android";
        URL parsed=new URL(url);
        if(!"https".equals(parsed.getProtocol())||!"ghmbcfqiomggoxgwghjs.supabase.co".equals(parsed.getHost()))
            throw new IOException("Untrusted update policy address.");
        HttpURLConnection c=(HttpURLConnection)parsed.openConnection();connection=c;
        c.setConnectTimeout(10000);c.setReadTimeout(10000);c.setInstanceFollowRedirects(false);
        c.setRequestProperty("apikey",CloudClient.PUBLIC_KEY);c.setRequestProperty("Accept","application/json");
        try{
            alive();int status=c.getResponseCode();
            if(status!=200)throw new IOException("Cannot verify the update rollout policy. Retry when connected.");
            if(c.getContentLengthLong()>8192)throw new IOException("Update policy response is too large.");
            try(InputStream in=c.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){
                byte[] buffer=new byte[2048];int n;
                while((n=in.read(buffer))!=-1){alive();if(out.size()+n>8192)throw new IOException("Update policy response is too large.");out.write(buffer,0,n);}
                JSONObject policy=new JSONObject(new String(out.toByteArray(),StandardCharsets.UTF_8));
                if(!"android".equals(policy.optString("platform"))||
                   !policy.has("minimumVersion")||!policy.has("rolloutPercent"))
                    throw new IOException("Invalid update rollout policy.");
                long floor=Long.parseLong(policy.getString("minimumVersion"));
                int percent=policy.getInt("rolloutPercent");
                Object candidate=policy.opt("candidateTag");
                if(floor<0||floor>Integer.MAX_VALUE||percent<0||percent>100||
                   (candidate!=null&&candidate!=JSONObject.NULL&&
                    (!(candidate instanceof String)||!((String)candidate).matches("android-v[0-9]+\\.[0-9]+\\.[0-9]+"))))
                    throw new IOException("Invalid update rollout policy.");
                return policy;
            }
        }finally{c.disconnect();connection=null;}
    }
    AndroidUpdateFeed check(Context c)throws Exception{
        alive();PackageInfo current=installed(c);
        if(!c.getPackageName().equals("com.lockvyn.app"))throw new Exception("Export an encrypted backup from this older app edition and import it into stable Lockvyn.");
        JSONObject policy=releasePolicy();
        String candidateTag=policy.isNull("candidateTag")?null:policy.getString("candidateTag");
        long minimumVersion=Long.parseLong(policy.getString("minimumVersion"));
        int rolloutPercent=policy.getInt("rolloutPercent");
        JSONArray releases=new JSONArray(text("https://api.github.com/repos/R3Dzf/BoshaVault/releases?per_page=50",512*1024));AndroidUpdateFeed best=null;
        for(int i=0;i<releases.length();i++){
            alive();JSONObject r=releases.getJSONObject(i);String tag=r.optString("tag_name");if(r.optBoolean("draft")||r.optBoolean("prerelease")||!tag.matches("android-v[0-9]+\\.[0-9]+\\.[0-9]+")||(candidateTag!=null&&!candidateTag.equals(tag)))continue;
            JSONArray assets=r.getJSONArray("assets");String feedUrl=null;boolean hasApk=false;
            for(int j=0;j<assets.length();j++){JSONObject a=assets.getJSONObject(j);if(a.optString("name").equals("android-update.json"))feedUrl=a.getString("browser_download_url");if(a.optString("name").equals("Lockvyn-Android.apk"))hasApk=true;}
            if(!hasApk||feedUrl==null)continue;
            if(!feedUrl.equals(AndroidUpdateFeed.REPOSITORY+"/releases/download/"+tag+"/android-update.json"))throw new Exception("Unexpected update metadata source.");
            AndroidUpdateFeed candidate=AndroidUpdateFeed.parse(text(feedUrl,16384),tag);if(best==null||candidate.versionCode>best.versionCode)best=candidate;
        }
        if(best==null)throw new Exception(candidateTag==null?"No stable Android release is published yet.":"The selected Android release is not available in the stable feed.");
        if(!best.signerSha256.equalsIgnoreCase(signer(current)))throw new Exception("This test edition cannot accept the stable release. Export and verify an encrypted backup before switching editions.");
        long installedVersion=current.getLongVersionCode();
        if(best.versionCode<=installedVersion){
            if(installedVersion<minimumVersion)throw new Exception("This app is below the required version, but no newer compatible release is available yet.");
            return null;
        }
        boolean mandatory=installedVersion<minimumVersion;
        // The rollout bucket is sampled locally for this check; no device ID or usage data is sent.
        if(!mandatory&&(rolloutPercent==0||java.security.ThreadLocalRandom.current().nextInt(100)>=rolloutPercent))return null;
        best.requireUpgrade(c.getPackageName(),installedVersion,signer(current),Build.VERSION.SDK_INT);return best;
    }
    private HttpURLConnection open(String url)throws Exception{
        for(int i=0;i<6;i++){
            alive();if(!AndroidUpdateFeed.allowedRedirect(url))throw new IOException("Untrusted update download address.");
            HttpURLConnection c=(HttpURLConnection)new URL(url).openConnection();connection=c;c.setConnectTimeout(15000);c.setReadTimeout(20000);c.setInstanceFollowRedirects(false);c.setRequestProperty("User-Agent","Lockvyn-Android-Updater");c.setRequestProperty("Accept","application/vnd.github+json, application/octet-stream");c.setRequestProperty("Accept-Encoding","identity");int status=c.getResponseCode();
            if(status>=300&&status<=399){String next=c.getHeaderField("Location");c.disconnect();if(next==null)throw new IOException("Missing update redirect.");url=new URL(new URL(url),next).toString();continue;}
            if(status!=200){c.disconnect();throw new IOException(status==403||status==429?"GitHub is limiting requests. Try again later.":"Update download failed (HTTP "+status+").");}return c;
        }throw new IOException("Too many update redirects.");
    }
    private String text(String url,int limit)throws Exception{
        HttpURLConnection c=open(url);try(InputStream in=c.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){
            byte[] buffer=new byte[8192];int n;while((n=in.read(buffer))!=-1){alive();if(out.size()+n>limit)throw new IOException("Update response is too large.");out.write(buffer,0,n);}return new String(out.toByteArray(),StandardCharsets.UTF_8);
        }finally{c.disconnect();connection=null;}
    }
    private File installedApk(Context c)throws Exception{
        PackageInfo app=installed(c);
        if(app.applicationInfo==null||app.applicationInfo.sourceDir==null)
            throw new IOException("Installed APK location is not available.");
        return new File(app.applicationInfo.sourceDir);
    }
    private AndroidUpdateFeed.Delta applicable(Context c,AndroidUpdateFeed feed){
        try{
            PackageInfo app=installed(c);
            File base=installedApk(c);
            return feed.forInstalled(app.getLongVersionCode(),AndroidDelta.sha256(base));
        }catch(Exception ignored){return null;}
    }
    long expectedDownload(Context c,AndroidUpdateFeed feed){
        AndroidUpdateFeed.Delta delta=applicable(c,feed);
        return delta==null?feed.size:delta.size;
    }
    private void downloadFile(String url,long size,String expected,File dest,Progress progress,int begin,int end)throws Exception{
        alive();HttpURLConnection request=open(url);
        try(InputStream in=request.getInputStream();FileOutputStream out=new FileOutputStream(dest)){
            MessageDigest digest=MessageDigest.getInstance("SHA-256");
            byte[] buffer=new byte[32768];long total=0;int n,last=-1;
            try{
                while((n=in.read(buffer))!=-1){
                    alive();total+=n;
                    if(total>size)throw new IOException("Downloaded file exceeds the signed release metadata.");
                    out.write(buffer,0,n);digest.update(buffer,0,n);
                    int p=begin+(int)(total*(end-begin)/size);
                    if(p!=last){progress.changed(p);last=p;}
                }
                out.getFD().sync();
                if(total!=size||!hex(digest.digest()).equalsIgnoreCase(expected))
                    throw new IOException("Update download failed checksum/size verification.");
                alive();
            }finally{java.util.Arrays.fill(buffer,(byte)0);}
        }finally{request.disconnect();connection=null;}
    }
    File download(Context c,AndroidUpdateFeed feed,Progress progress)throws Exception{
        alive();
        PackageInfo installed=installed(c);
        feed.requireUpgrade(c.getPackageName(),installed.getLongVersionCode(),signer(installed),Build.VERSION.SDK_INT);
        File target=apk(c),partial=new File(c.getCacheDir(),"lockvyn-update.part");
        File patch=new File(c.getCacheDir(),"lockvyn-update.delta.part");
        boolean complete=false;
        target.delete();partial.delete();patch.delete();
        try{
            AndroidUpdateFeed.Delta delta=applicable(c,feed);
            if(delta!=null&&c.getCacheDir().getUsableSpace()>feed.size+delta.size+2*1024*1024L){
                try{
                    downloadFile(delta.url,delta.size,delta.sha256,patch,progress,0,65);
                    alive();progress.changed(68);
                    AndroidDelta.apply(installedApk(c),patch,partial,delta.fromSha256,feed.sha256,feed.size,()->alive());
                    alive();progress.changed(94);
                    verify(c,partial,feed);
                    if(!partial.renameTo(target))throw new IOException("Could not retain reconstructed Android update.");
                    alive();complete=true;progress.changed(100);return target;
                }catch(Exception e){
                    if(cancelled.get()||Thread.currentThread().isInterrupted())throw e;
                    // A stale base, invalid patch or damaged archive must never modify the installed APK.
                    // Only the FULL, separately verified signed APK may be installed as fallback.
                    patch.delete();partial.delete();target.delete();progress.changed(0);
                }finally{patch.delete();}
            }
            if(c.getCacheDir().getUsableSpace()<feed.size+2*1024*1024L)
                throw new IOException("Not enough free storage for an Android update.");
            downloadFile(feed.url,feed.size,feed.sha256,partial,progress,0,98);
            verify(c,partial,feed);
            if(!partial.renameTo(target))throw new IOException("Could not finish the Android update.");
            alive();complete=true;progress.changed(100);return target;
        }finally{
            patch.delete();partial.delete();
            if(!complete)target.delete();
        }
    }
    static void verify(Context c,File file,AndroidUpdateFeed feed)throws Exception{
        PackageInfo current=installed(c);feed.requireUpgrade(c.getPackageName(),current.getLongVersionCode(),signer(current),Build.VERSION.SDK_INT);
        if(!file.isFile()||file.length()!=feed.size)throw new Exception("Update file is incomplete.");MessageDigest digest=MessageDigest.getInstance("SHA-256");try(InputStream in=new FileInputStream(file)){byte[] b=new byte[32768];int n;while((n=in.read(b))!=-1)digest.update(b,0,n);}
        if(!hex(digest.digest()).equalsIgnoreCase(feed.sha256))throw new Exception("Update checksum failed. Download again.");
        PackageInfo archive=c.getPackageManager().getPackageArchiveInfo(file.getAbsolutePath(),PackageManager.GET_SIGNING_CERTIFICATES);
        if(archive==null||!feed.packageName.equals(archive.packageName)||archive.getLongVersionCode()!=feed.versionCode||!feed.versionName.equals(archive.versionName))throw new Exception("Downloaded APK identity differs from its release.");
        if(archive.applicationInfo==null||archive.applicationInfo.minSdkVersion!=feed.minSdk)throw new Exception("APK Android requirements differ from its release.");
        if(!signer(archive).equalsIgnoreCase(signer(current))||!signer(archive).equalsIgnoreCase(feed.signerSha256))throw new Exception("This APK was signed by a different publisher.");
        // Android's package installer performs cryptographic APK signature verification.
    }
}
