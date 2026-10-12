package com.bosha.vault.tests;

import android.app.*;
import android.content.*;
import android.content.pm.PackageInfo;
import android.net.Uri;
import android.os.*;
import android.widget.*;
import java.io.*;
import java.lang.reflect.*;
import java.nio.file.Files;
import java.security.MessageDigest;
import org.json.*;

/** Disposable emulator acceptance. No network, email or production signing key. */
public final class UpdateSmoke extends Instrumentation {
    private String mode;private int count;private ClassLoader loader;private Context target;
    private static final String PASS="synthetic emulator master passphrase 2026";
    @Override public void onCreate(Bundle args){super.onCreate(args);mode=args.getString("mode","seed");start();}
    interface Work{void run()throws Exception;}
    private void yes(boolean valid,String name)throws Exception{if(!valid)throw new Exception(name);count++;}
    private void no(Work work,String name)throws Exception{try{work.run();}catch(Exception expected){count++;return;}throw new Exception("Accepted "+name);}
    private Method method(Class<?> c,String name,Class<?>...args)throws Exception{Method m=c.getDeclaredMethod(name,args);m.setAccessible(true);return m;}
    private static String hash(byte[] bytes)throws Exception{StringBuilder b=new StringBuilder();for(byte x:MessageDigest.getInstance("SHA-256").digest(bytes))b.append(String.format(java.util.Locale.ROOT,"%02x",x&255));return b.toString();}
    private File asset(String name)throws Exception{File f=new File(target.getCacheDir(),name);try(InputStream in=getContext().getAssets().open(name);OutputStream out=new FileOutputStream(f)){byte[] b=new byte[32768];int n;while((n=in.read(b))!=-1)out.write(b,0,n);}return f;}
    private Object feed(File file,long code,String version)throws Exception{
        PackageInfo current=target.getPackageManager().getPackageInfo(target.getPackageName(),android.content.pm.PackageManager.GET_SIGNING_CERTIFICATES);
        String signer=(String)method(loader.loadClass("com.bosha.vault.AndroidUpdater"),"signer",PackageInfo.class).invoke(null,current);
        String tag="android-v"+version;JSONObject json=new JSONObject().put("schema",1).put("packageName","com.lockvyn.app").put("versionName",version).put("versionCode",code).put("minSdk",28).put("tag",tag).put("size",file.length()).put("sha256",hash(Files.readAllBytes(file.toPath()))).put("signerSha256",signer).put("url","https://github.com/R3Dzf/BoshaVault/releases/download/"+tag+"/Lockvyn-Android.apk");
        return method(loader.loadClass("com.bosha.vault.AndroidUpdateFeed"),"parse",String.class,String.class).invoke(null,json.toString(),tag);
    }
    @Override public void onStart(){Bundle result=new Bundle();int status=Activity.RESULT_CANCELED;
        try{target=getTargetContext();loader=target.getClassLoader();PackageInfo installed=target.getPackageManager().getPackageInfo(target.getPackageName(),0);
            if(!target.getPackageName().equals("com.lockvyn.app")||!installed.versionName.startsWith("9.0."))throw new Exception("Update acceptance requires the isolated synthetic-version app.");
            File vault=new File(target.getFilesDir(),"vault.boshavault"),device=new File(target.getFilesDir(),"device.id"),proof=new File(target.getFilesDir(),"update-proof.json");Class<?> store=loader.loadClass("com.bosha.vault.Store");Object engine=method(store,"engine",Context.class).invoke(null,target);Class<?> type=engine.getClass();
            if(mode.equals("seed")){
                if(!vault.exists())method(type,"create",String.class).invoke(engine,PASS);else method(type,"open",String.class).invoke(engine,PASS);
                JSONObject entry=(JSONObject)method(type,"blank").invoke(null);entry.put("title","Synthetic update-preservation login").put("username","demo@example.invalid").put("password","Synthetic update test only");method(type,"upsert",JSONObject.class).invoke(engine,entry);method(type,"close").invoke(engine);
                target.getSharedPreferences("update-acceptance",Context.MODE_PRIVATE).edit().putString("marker","preserved synthetic preference").commit();
                JSONObject saved=new JSONObject().put("vaultSha256",hash(Files.readAllBytes(vault.toPath()))).put("device",new String(Files.readAllBytes(device.toPath()),"UTF-8")).put("entry",entry.getString("id"));Files.write(proof.toPath(),saved.toString().getBytes("UTF-8"));
                File update=asset("update.apk"),wrong=asset("wrong-signer.apk");Class<?> updater=loader.loadClass("com.bosha.vault.AndroidUpdater"),feedType=loader.loadClass("com.bosha.vault.AndroidUpdateFeed");Method verify=method(updater,"verify",Context.class,File.class,feedType);Object candidate=feed(update,9001,"9.0.1");verify.invoke(null,target,update,candidate);yes(true,"valid same-signer APK accepted");
                no(()->verify.invoke(null,target,wrong,feed(wrong,9002,"9.0.2")),"wrong APK signing certificate");
                byte[] tampered=Files.readAllBytes(update.toPath());tampered[tampered.length/2]^=1;File damaged=new File(target.getCacheDir(),"damaged.apk");Files.write(damaged.toPath(),tampered);no(()->verify.invoke(null,target,damaged,candidate),"damaged APK");damaged.delete();
                File shared=new File(target.getCacheDir(),"lockvyn-update.apk");Files.copy(update.toPath(),shared.toPath(),java.nio.file.StandardCopyOption.REPLACE_EXISTING);
                try(InputStream in=target.getContentResolver().openInputStream(Uri.parse("content://com.lockvyn.app.updates/update.apk"))){ByteArrayOutputStream bytes=new ByteArrayOutputStream();byte[] b=new byte[32768];int n;while((n=in.read(b))!=-1)bytes.write(b,0,n);yes(hash(bytes.toByteArray()).equals(hash(Files.readAllBytes(update.toPath()))),"provider exposes only the cached APK");}
                no(()->target.getContentResolver().openInputStream(Uri.parse("content://com.lockvyn.app.updates/../files/vault.boshavault")),"provider path traversal");
                no(()->target.getContentResolver().openOutputStream(Uri.parse("content://com.lockvyn.app.updates/update.apk")),"provider writes");
                yes(!target.getPackageManager().canRequestPackageInstalls(),"install-source permission starts denied");
            }else if(mode.equals("verify")){
                JSONObject saved=new JSONObject(new String(Files.readAllBytes(proof.toPath()),"UTF-8"));yes(installed.getLongVersionCode()==9001,"new APK installed in place");yes(hash(Files.readAllBytes(vault.toPath())).equals(saved.getString("vaultSha256")),"encrypted vault bytes preserved");yes(new String(Files.readAllBytes(device.toPath()),"UTF-8").equals(saved.getString("device")),"device identity preserved");yes(target.getSharedPreferences("update-acceptance",Context.MODE_PRIVATE).getString("marker","").equals("preserved synthetic preference"),"app preferences preserved");
                method(type,"open",String.class).invoke(engine,PASS);JSONObject body=(JSONObject)method(type,"snapshot").invoke(engine);JSONArray entries=body.getJSONArray("entries");boolean found=false;for(int i=0;i<entries.length();i++)if(entries.getJSONObject(i).optString("id").equals(saved.getString("entry")))found=true;yes(found,"vault reopens and saved login remains");method(type,"close").invoke(engine);
                File candidate=asset("update.apk");Class<?> updater=loader.loadClass("com.bosha.vault.AndroidUpdater"),feedType=loader.loadClass("com.bosha.vault.AndroidUpdateFeed");no(()->method(updater,"verify",Context.class,File.class,feedType).invoke(null,target,candidate,feed(candidate,9001,"9.0.1")),"same-version reinstall through updater");
            }else throw new Exception("Unknown acceptance mode.");
            result.putString("stream","UPDATE_SMOKE_PASS: "+mode+" — "+count+" real Android update/signature/provider/data checks passed.\n");status=Activity.RESULT_OK;
        }catch(Throwable e){result.putString("stream","UPDATE_SMOKE_FAIL: "+android.util.Log.getStackTraceString(e)+"\n");}finish(status,result);
    }
}
