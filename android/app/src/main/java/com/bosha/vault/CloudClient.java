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
    CloudClient(Guard guard){this.guard=guard;}
    private void check()throws Exception{if(cancelled)throw new IOException("Cloud operation cancelled.");guard.check();}
    private byte[] request(String method,String path,JSONObject body,String access,int limit)throws Exception{
        check();HttpsURLConnection conn=(HttpsURLConnection)new URL(URL+path).openConnection();connection=conn;
        conn.setInstanceFollowRedirects(false);conn.setConnectTimeout(15000);conn.setReadTimeout(30000);conn.setRequestMethod(method);conn.setRequestProperty("apikey",PUBLIC_KEY);
        if(access!=null)conn.setRequestProperty("Authorization","Bearer "+access);
        byte[] payload=null;
        try{
            if(body!=null){conn.setDoOutput(true);conn.setRequestProperty("Content-Type","application/json");conn.setRequestProperty("Prefer","resolution=merge-duplicates,return=minimal");payload=body.toString().getBytes(StandardCharsets.UTF_8);conn.setFixedLengthStreamingMode(payload.length);try(OutputStream out=conn.getOutputStream()){out.write(payload);}}
            int status=conn.getResponseCode();check();if(status<200||status>=300){String code=errorCode(conn);throw new Exception(status==429?"Cloud rate limit reached. Wait before trying again.":code.equals("email_address_not_authorized")?"Email verification delivery is not enabled for this address yet. The app owner needs to configure the email sender. Your vault works offline without an account.":code.equals("email_not_confirmed")?"Confirm your account email first. Use Resend confirmation email if needed.":(path.equals("auth/v1/signup")||path.equals("auth/v1/resend"))?"Cloud verification could not be completed. Check the email and password requirements, or try again later. Your offline vault is unchanged.":"Cloud request was rejected. Check your sign-in and confirm your email.");}
            if(conn.getContentLengthLong()>limit)throw new Exception("Cloud response exceeds its size limit.");
            try(InputStream in=conn.getInputStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){
                byte[] chunk=new byte[8192];int read;try{while((read=in.read(chunk))!=-1){check();if(out.size()+read>limit)throw new Exception("Cloud response exceeds its size limit.");out.write(chunk,0,read);}return out.toByteArray();}finally{Arrays.fill(chunk,(byte)0);}
            }
        }finally{if(payload!=null)Arrays.fill(payload,(byte)0);conn.disconnect();connection=null;}
    }
    private static String errorCode(HttpsURLConnection conn){
        byte[] bytes=null;try(InputStream in=conn.getErrorStream();ByteArrayOutputStream out=new ByteArrayOutputStream()){
            if(in==null)return "";byte[] chunk=new byte[1024];int n;try{while((n=in.read(chunk))!=-1){if(out.size()+n>65536)return "";out.write(chunk,0,n);}bytes=out.toByteArray();return StrictJson.object(new String(bytes,StandardCharsets.UTF_8)).optString("code","");}finally{Arrays.fill(chunk,(byte)0);}
        }catch(Exception ignored){return "";}finally{if(bytes!=null)Arrays.fill(bytes,(byte)0);}
    }
    void resendConfirmation(String email)throws Exception{
        if(email.length()<3||email.length()>320||!email.contains("@"))throw new Exception("Enter your cloud account email.");byte[] verifier=new byte[32];new java.security.SecureRandom().nextBytes(verifier);
        try{String encoded=Base64.getUrlEncoder().withoutPadding().encodeToString(verifier);String challenge=Base64.getUrlEncoder().withoutPadding().encodeToString(MessageDigest.getInstance("SHA-256").digest(encoded.getBytes(StandardCharsets.US_ASCII)));byte[] result=request("POST","auth/v1/resend",new JSONObject().put("type","signup").put("email",email).put("code_challenge",challenge).put("code_challenge_method","s256"),null,65536);Arrays.fill(result,(byte)0);}finally{Arrays.fill(verifier,(byte)0);}
    }
    static final class Session {
        final String owner,access,refresh;
        Session(byte[] bytes)throws Exception{
            try{JSONObject j=StrictJson.object(new String(bytes,StandardCharsets.UTF_8));owner=j.getJSONObject("user").getString("id");access=j.getString("access_token");refresh=j.getString("refresh_token");
                if(!VaultEngine.id(owner)||access.length()<20||access.length()>8192||refresh.length()<10||refresh.length()>8192||access.matches("(?s).*\\p{Cntrl}.*")||refresh.matches("(?s).*\\p{Cntrl}.*"))throw new Exception("Cloud returned an invalid session.");
            }catch(Exception invalid){throw new Exception("Cloud account returned an invalid session.");}
            finally{Arrays.fill(bytes,(byte)0);}
        }
    }
    Session signIn(String email,String password)throws Exception{
        if(email.length()<3||email.length()>320||!email.contains("@")||password.length()<1||password.length()>1024)throw new Exception("Enter your cloud account email and password.");
        return new Session(request("POST","auth/v1/token?grant_type=password",new JSONObject().put("email",email).put("password",password),null,65536));
    }
    void signUp(String email,String password)throws Exception{
        if(email.length()<3||email.length()>320||!email.contains("@")||password.length()<16||password.length()>1024)throw new Exception("Use a valid email and a separate cloud password of at least 16 characters.");
        byte[] verifier=new byte[32];new java.security.SecureRandom().nextBytes(verifier);
        try{
            String encoded=Base64.getUrlEncoder().withoutPadding().encodeToString(verifier);
            String challenge=Base64.getUrlEncoder().withoutPadding().encodeToString(MessageDigest.getInstance("SHA-256").digest(encoded.getBytes(StandardCharsets.US_ASCII)));
            byte[] result=request("POST","auth/v1/signup",new JSONObject().put("email",email).put("password",password).put("code_challenge",challenge).put("code_challenge_method","s256"),null,65536);Arrays.fill(result,(byte)0);
        }finally{Arrays.fill(verifier,(byte)0);}
    }
    private Session refresh(JSONObject profile,CloudSettingsStore store)throws Exception{
        Session signedIn=new Session(request("POST","auth/v1/token?grant_type=refresh_token",new JSONObject().put("refresh_token",profile.getString("refreshToken")),null,65536));
        check();if(!signedIn.owner.equals(profile.getString("ownerId")))throw new Exception("Cloud account changed. Reconnect this device.");
        profile.put("refreshToken",signedIn.refresh);store.save(profile);return signedIn;
    }
    int sync(VaultEngine engine,JSONObject profile,CloudSettingsStore store)throws Exception{
        Session signedIn=refresh(profile,store);String slot=profile.getString("vaultSlot"),device=profile.getString("deviceSlot");
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
    void signOut(JSONObject profile,CloudSettingsStore store)throws Exception{Session signedIn=refresh(profile,store);byte[] response=request("POST","auth/v1/logout?scope=local",new JSONObject(),signedIn.access,65536);Arrays.fill(response,(byte)0);check();store.forget();}
    private static String hex(byte[] bytes){StringBuilder result=new StringBuilder();for(byte b:bytes)result.append(String.format(Locale.ROOT,"%02x",b&255));return result.toString();}
    @Override public void close(){cancelled=true;HttpsURLConnection conn=connection;if(conn!=null)conn.disconnect();}
}
