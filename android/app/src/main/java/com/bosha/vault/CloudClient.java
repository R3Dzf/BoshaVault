package com.bosha.vault;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.time.Instant;
import java.util.*;
import javax.net.ssl.HttpsURLConnection;
import org.json.*;

final class CloudClient implements AutoCloseable {
    static final String URL="https://ghmbcfqiomggoxgwghjs.supabase.co/";
    static final String PUBLIC_KEY="sb_publishable__Kb9CeN7Bcx3lxz4VDL1hQ_27hJVAee";
    interface Guard {void check()throws Exception;}
    private final Guard guard;
    private volatile HttpsURLConnection connection;
    private volatile boolean cancelled;
    final CloudAuth auth;
    CloudClient(Guard guard){this.guard=guard;auth=new CloudAuth((method,path,body,access)->request(method,path,body,access,method.equals("POST")&&path.equals("auth/v1/factors")?524288:65536));}
    private void check()throws Exception{if(cancelled)throw new IOException("Cloud operation cancelled.");guard.check();}
    private byte[] request(String method,String path,JSONObject body,String access,int limit)throws Exception{
        check();HttpsURLConnection conn=(HttpsURLConnection)new URL(URL+path).openConnection();connection=conn;
        conn.setInstanceFollowRedirects(false);conn.setConnectTimeout(15000);conn.setReadTimeout(30000);conn.setRequestMethod(method);conn.setRequestProperty("apikey",PUBLIC_KEY);
        if(access!=null)conn.setRequestProperty("Authorization","Bearer "+access);
        byte[] payload=null;
        try{
            if(body!=null){conn.setDoOutput(true);conn.setRequestProperty("Content-Type","application/json");conn.setRequestProperty("Prefer","resolution=merge-duplicates,return=minimal");payload=body.toString().getBytes(StandardCharsets.UTF_8);conn.setFixedLengthStreamingMode(payload.length);try(OutputStream out=conn.getOutputStream()){out.write(payload);}}
            int status=conn.getResponseCode();check();if(status<200||status>=300){String code=errorCode(conn);throw new Exception(CloudAuth.error(status,path,code));}
            if(conn.getContentLengthLong()>limit)throw new Exception("Cloud response exceeds its size limit.");
            try(InputStream in=conn.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){
                byte[] chunk=new byte[8192];int read;try{while((read=in.read(chunk))!=-1){check();if(out.size()+read>limit)throw new Exception("Cloud response exceeds its size limit.");out.write(chunk,0,read);}return out.toByteArray();}finally{Arrays.fill(chunk,(byte)0);}
            }
        }finally{if(payload!=null)Arrays.fill(payload,(byte)0);conn.disconnect();connection=null;}
    }
    private static String errorCode(HttpsURLConnection conn){
        byte[] bytes=null;try(InputStream in=conn.getErrorStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){
            if(in==null)return "";byte[] chunk=new byte[1024];int n;try{while((n=in.read(chunk))!=-1){if(out.size()+n>65536)return "";out.write(chunk,0,n);}bytes=out.toByteArray();JSONObject error=StrictJson.object(new String(bytes,StandardCharsets.UTF_8));return error.optString("error_code",error.optString("code",""));}finally{Arrays.fill(chunk,(byte)0);}
        }catch(Exception ignored){return "";}finally{if(bytes!=null)Arrays.fill(bytes,(byte)0);}
    }
    void resendConfirmation(String email)throws Exception{auth.resendConfirmation(email);}
    CloudAuth.Session signIn(String email,String password)throws Exception{return auth.signIn(email,password);}
    void signUp(String email,String password)throws Exception{auth.signUp(email,password);}
    CloudAuth.Session refresh(JSONObject profile,CloudSettingsStore store)throws Exception{
        CloudAuth.Session signedIn=auth.refresh(profile.getString("refreshToken"));
        saveSession(signedIn,profile,store);return signedIn;
    }
    void saveSession(CloudAuth.Session session,JSONObject profile,CloudSettingsStore store)throws Exception{
        check();if(!session.owner.equals(profile.getString("ownerId")))throw new Exception("Cloud account changed. Reconnect this device.");
        profile.put("refreshToken",session.refresh);store.save(profile);
    }
    int sync(VaultEngine engine,JSONObject profile,CloudSettingsStore store,CloudAuth.Session signedIn)throws Exception{
        check();if(!signedIn.owner.equals(profile.getString("ownerId")))throw new Exception("Cloud account changed.");String slot=profile.getString("vaultSlot"),device=profile.getString("deviceSlot");
        byte[] response=request("GET","rest/v1/opaque_device_snapshots?select=device_slot,ciphertext_base64,ciphertext_sha256&vault_slot=eq."+slot+"&order=updated_at.desc&limit=64",null,signedIn.access,16000000);
        byte[] key=CloudSnapshot.parseKey(profile.getString("recoveryKey")),local=null,outer=null;List<byte[]> snapshots=new ArrayList<>();
        try{
            JSONArray rows=StrictJson.array(new String(response,StandardCharsets.UTF_8));if(rows.length()>64)throw new Exception("Too many cloud devices.");
            for(int i=0;i<rows.length();i++){
                JSONObject row=rows.getJSONObject(i);String encoded=row.getString("ciphertext_base64"),digest=row.getString("ciphertext_sha256"),remote=row.getString("device_slot");
                if(encoded.length()>2800000||!digest.matches("[0-9a-f]{64}"))throw new Exception("Invalid cloud backup metadata.");
                byte[] blob=Base64.getDecoder().decode(encoded);if(blob.length>CloudSnapshot.MAX_INNER+29||!MessageDigest.isEqual(hex(VaultEngine.sha(blob)).getBytes(StandardCharsets.US_ASCII),digest.getBytes(StandardCharsets.US_ASCII)))throw new Exception("Cloud backup checksum failed.");
                snapshots.add(CloudSnapshot.open(blob,key,slot,remote));
            }
            check();int conflicts=engine.mergeCloud(snapshots);check();local=engine.export();outer=CloudSnapshot.seal(local,key,slot,device);
            JSONObject body=new JSONObject().put("owner_id",signedIn.owner).put("vault_slot",slot).put("device_slot",device).put("ciphertext_base64",Base64.getEncoder().encodeToString(outer)).put("ciphertext_sha256",hex(VaultEngine.sha(outer)));
            byte[] reply=request("POST","rest/v1/opaque_device_snapshots?on_conflict=owner_id,vault_slot,device_slot",body,signedIn.access,65536);Arrays.fill(reply,(byte)0);
            check();profile.put("lastSyncUtc",Instant.now().toString());store.save(profile);return conflicts;
        }finally{Arrays.fill(response,(byte)0);Arrays.fill(key,(byte)0);if(local!=null)Arrays.fill(local,(byte)0);if(outer!=null)Arrays.fill(outer,(byte)0);for(byte[] inner:snapshots)Arrays.fill(inner,(byte)0);}
    }
    void signOut(CloudAuth.Session signedIn,CloudSettingsStore store)throws Exception{auth.signOut(signedIn);check();store.forget();}
    private static String hex(byte[] bytes){StringBuilder result=new StringBuilder();for(byte b:bytes)result.append(String.format(Locale.ROOT,"%02x",b&255));return result.toString();}
    @Override public void close(){cancelled=true;HttpsURLConnection conn=connection;if(conn!=null)conn.disconnect();}
}
