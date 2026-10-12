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
    AndroidUpdateFeed check(Context c)throws Exception{
        alive();PackageInfo current=installed(c);
        if(!c.getPackageName().equals("com.lockvyn.app"))throw new Exception("Export an encrypted backup from this older app edition and import it into stable Lockvyn.");
        JSONArray releases=new JSONArray(text("https://api.github.com/repos/R3Dzf/BoshaVault/releases?per_page=50",512*1024));AndroidUpdateFeed best=null;
        for(int i=0;i<releases.length();i++){
            alive();JSONObject r=releases.getJSONObject(i);String tag=r.optString("tag_name");if(r.optBoolean("draft")||r.optBoolean("prerelease")||!tag.matches("android-v[0-9]+\\.[0-9]+\\.[0-9]+"))continue;
            JSONArray assets=r.getJSONArray("assets");String feedUrl=null;boolean hasApk=false;
            for(int j=0;j<assets.length();j++){JSONObject a=assets.getJSONObject(j);if(a.optString("name").equals("android-update.json"))feedUrl=a.getString("browser_download_url");if(a.optString("name").equals("Lockvyn-Android.apk"))hasApk=true;}
            if(!hasApk||feedUrl==null)continue;
            if(!feedUrl.equals(AndroidUpdateFeed.REPOSITORY+"/releases/download/"+tag+"/android-update.json"))throw new Exception("Unexpected update metadata source.");
            AndroidUpdateFeed candidate=AndroidUpdateFeed.parse(text(feedUrl,16384),tag);if(best==null||candidate.versionCode>best.versionCode)best=candidate;
        }
        if(best==null)throw new Exception("No stable Android release is published yet.");
        if(!best.signerSha256.equalsIgnoreCase(signer(current)))throw new Exception("This test edition cannot accept the stable release. Export and verify an encrypted backup before switching editions.");
        if(best.versionCode<=current.getLongVersionCode())return null;
        best.requireUpgrade(c.getPackageName(),current.getLongVersionCode(),signer(current),Build.VERSION.SDK_INT);return best;
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
    File download(Context c,AndroidUpdateFeed feed,Progress progress)throws Exception{
        alive();feed.requireUpgrade(c.getPackageName(),installed(c).getLongVersionCode(),signer(installed(c)),Build.VERSION.SDK_INT);
        File target=apk(c),partial=new File(c.getCacheDir(),"lockvyn-update.part");boolean complete=false;
        if(c.getCacheDir().getUsableSpace()<feed.size*2+2*1024*1024)throw new IOException("Not enough free space for the update.");
        HttpURLConnection request=open(feed.url);try(InputStream in=request.getInputStream();FileOutputStream out=new FileOutputStream(partial)){
            byte[] buffer=new byte[32768];long total=0;int n,last=-1;while((n=in.read(buffer))!=-1){alive();total+=n;if(total>feed.size)throw new IOException("Update size differs from the release.");out.write(buffer,0,n);int percent=(int)(total*100/feed.size);if(percent!=last){progress.changed(percent);last=percent;}}
            out.getFD().sync();if(total!=feed.size)throw new IOException("The download is incomplete.");alive();verify(c,partial,feed);
            if(target.exists()&&!target.delete())throw new IOException("Cannot replace the cached update.");if(!partial.renameTo(target))throw new IOException("Cannot finish the update download.");alive();complete=true;return target;
        }finally{request.disconnect();connection=null;if(!complete){partial.delete();target.delete();}}
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
