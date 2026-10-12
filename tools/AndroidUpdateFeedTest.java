package com.bosha.vault;
import org.json.*;
public final class AndroidUpdateFeedTest {
 static int passed;
 interface Work{void run()throws Exception;}
 static void yes(boolean result,String name)throws Exception{if(!result)throw new Exception(name);passed++;}
 static void no(Work action,String name)throws Exception{try{action.run();}catch(Exception expected){passed++;return;}throw new Exception("Accepted "+name);}
 static JSONObject valid()throws Exception{return new JSONObject().put("schema",1).put("packageName","com.lockvyn.app").put("versionName","1.7.0").put("versionCode",12).put("minSdk",28).put("tag","android-v1.7.0").put("size",2500000).put("sha256","a".repeat(64)).put("signerSha256","b".repeat(64)).put("url",AndroidUpdateFeed.REPOSITORY+"/releases/download/android-v1.7.0/Lockvyn-Android.apk");}
 static AndroidUpdateFeed parse(JSONObject o)throws Exception{return AndroidUpdateFeed.parse(o.toString(),"android-v1.7.0");}
 public static void main(String[] args)throws Exception{
  AndroidUpdateFeed f=parse(valid());f.requireUpgrade("com.lockvyn.app",11,"b".repeat(64),28);yes(f.versionCode==12,"newer compatible update");
  no(()->f.requireUpgrade("com.lockvyn.app",12,"b".repeat(64),35),"same version");no(()->f.requireUpgrade("com.lockvyn.app",13,"b".repeat(64),35),"downgrade");
  no(()->f.requireUpgrade("com.bosha.vault",11,"b".repeat(64),35),"package replacement");no(()->f.requireUpgrade("com.lockvyn.app",11,"c".repeat(64),35),"certificate change");no(()->f.requireUpgrade("com.lockvyn.app",11,"b".repeat(64),27),"unsupported Android");
  for(String field:new String[]{"schema","size","versionCode","minSdk"})no(()->parse(valid().put(field,"12")),"numeric string "+field);
  no(()->parse(valid().put("versionCode",12.5)),"fractional version");no(()->parse(valid().put("versionCode",2147483648L)),"version overflow");no(()->parse(valid().put("minSdk",4294967324L)),"SDK overflow");no(()->parse(valid().put("size",AndroidUpdateFeed.MAX_APK+1)),"oversized APK");no(()->parse(valid().put("size",0)),"empty APK");
  no(()->parse(valid().put("sha256","a")),"invalid checksum");no(()->parse(valid().put("signerSha256","bad")),"invalid signer fingerprint");no(()->parse(valid().put("schema",2)),"unknown schema");no(()->parse(valid().put("tag","windows-v1.7.0")),"Windows channel");
  no(()->parse(valid().put("url","https://example.com/Lockvyn.apk")),"foreign source");no(()->parse(valid().put("url",f.url+"?token=x")),"query override");no(()->AndroidUpdateFeed.parse(valid().toString(),"android-v1.8.0"),"tag disagreement");
  no(()->AndroidUpdateFeed.parse(valid().toString()+" trailing",f.tag),"trailing JSON");no(()->AndroidUpdateFeed.parse(valid().toString().replace("\"versionCode\":12","\"versionCode\":12,\"versionCode\":13"),f.tag),"duplicate metadata key");
  JSONObject delta=new JSONObject().put("fromVersionCode",11).put("fromSha256","c".repeat(64))
    .put("sha256","d".repeat(64)).put("size",140000)
    .put("url",AndroidUpdateFeed.REPOSITORY+"/releases/download/android-v1.7.0/Lockvyn-Android-from-11.lvdelta.gz");
  AndroidUpdateFeed supported=parse(valid().put("deltas",new JSONArray().put(delta)));
  yes(supported.forInstalled(11,"c".repeat(64))!=null,"same official APK can use pinned delta");
  yes(supported.forInstalled(11,"d".repeat(64))==null&&supported.forInstalled(10,"c".repeat(64))==null,"mismatched installed APK selects full fallback");
  no(()->parse(valid().put("deltas",new JSONArray().put(new JSONObject(delta.toString()).put("size",2500000)))),"delta not smaller than APK");
  no(()->parse(valid().put("deltas",new JSONArray().put(new JSONObject(delta.toString()).put("url","https://other.invalid/file")))),"foreign delta host");
  no(()->parse(valid().put("deltas",new JSONArray().put(new JSONObject(delta.toString()).put("fromVersionCode",12)))),"same-version delta base");
  no(()->parse(valid().put("deltas",new JSONArray().put(new JSONObject(delta.toString()).put("fromSha256","wrong")))),"delta missing baseline fingerprint");
  no(()->parse(valid().put("deltas",new JSONArray().put(delta).put(delta))),"duplicate delta baseline");
  for(String u:new String[]{"http://github.com/a","https://github.com.evil.invalid/a","https://github.com@evil.invalid/a","https://127.0.0.1/a","https://api.github.com:8443/a","file:///tmp/update.apk"})yes(!AndroidUpdateFeed.allowedRedirect(u),"unsafe redirect");
  yes(AndroidUpdateFeed.allowedRedirect("https://release-assets.githubusercontent.com/a"),"GitHub CDN");yes(AndroidUpdateFeed.allowedRedirect("https://api.github.com/repos/a/b/releases"),"GitHub API");
  System.out.println("RESULT: "+passed+" Android update metadata, version, signature and source checks passed.");
 }
}
