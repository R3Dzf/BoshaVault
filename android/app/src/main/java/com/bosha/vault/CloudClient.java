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
    static final Object GATE=new Object();
    static final String URL="https://ghmbcfqiomggoxgwghjs.supabase.co/";
    static final String PUBLIC_KEY="sb_publishable__Kb9CeN7Bcx3lxz4VDL1hQ_27hJVAee";
    interface Guard {void check()throws Exception;}
    private final Guard guard;
    private volatile HttpsURLConnection connection;
    private volatile boolean cancelled;
    private volatile boolean persistingRefresh;
    final CloudAuth auth;
    CloudClient(Guard guard){this.guard=guard;auth=new CloudAuth((method,path,body,access)->request(method,path,body,access,method.equals("POST")&&path.equals("auth/v1/factors")?524288:65536));}
    private void check()throws Exception{if(persistingRefresh)return;if(cancelled)throw new IOException("Cloud operation cancelled.");guard.check();}
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
        synchronized(GATE){check();JSONObject latest=store.load(profile.getString("vaultId"));if(latest==null)throw new Exception("Cloud is disconnected on this device.");
            persistingRefresh=true;
            try{CloudAuth.Session signedIn=auth.refresh(latest.getString("refreshToken"));
                profile.put("refreshToken",signedIn.refresh);latest.put("refreshToken",signedIn.refresh);store.save(latest);return signedIn;}
            finally{persistingRefresh=false;CloudSettingsStore.clear(latest);if(cancelled){HttpsURLConnection conn=connection;if(conn!=null)conn.disconnect();}}
        }
    }
    void saveSession(CloudAuth.Session session,JSONObject profile,CloudSettingsStore store)throws Exception{
        synchronized(GATE){if(!session.owner.equals(profile.getString("ownerId")))throw new Exception("Cloud account changed. Reconnect this device.");
            JSONObject latest=store.load(profile.getString("vaultId"));if(latest==null)throw new Exception("Cloud is disconnected on this device.");
            profile.put("refreshToken",session.refresh);latest.put("refreshToken",session.refresh);store.save(latest);CloudSettingsStore.clear(latest);}
    }
    int sync(VaultEngine engine,JSONObject profile,CloudSettingsStore store,CloudAuth.Session signedIn)throws Exception{
        return syncAutomatic(engine,null,profile,store,signedIn);
    }
    int syncAutomatic(VaultEngine engine,File vault,JSONObject profile,CloudSettingsStore store,CloudAuth.Session session)throws Exception{
        synchronized(GATE){
            check();if(!session.owner.equals(profile.getString("ownerId")))throw new Exception("Cloud account changed.");
            JSONObject latest=store.load(profile.getString("vaultId"));if(latest==null)throw new Exception("Cloud is disconnected on this device.");
            // Keep a token rotated by another foreground action before writing checkpoints.
            profile.put("refreshToken",latest.getString("refreshToken"));
            for(String field:new String[]{"lastUploadedHash","lastOuterHash","downloadedStamp","mergedStamp","keyEpoch","autoSync"})if(latest.has(field))profile.put(field,latest.get(field));
            String slot=profile.getString("vaultSlot"),device=profile.getString("deviceSlot");byte[] local=null,key=null,outer=null,response=null;
            List<byte[]> snapshots=new ArrayList<>();int conflicts=0;boolean changed=false;
            try{
                local=engine!=null&&engine.isOpen()?engine.export():VaultEngine.read(vault);
                JSONObject env=VaultEngine.parse(local);if(!env.getString("vaultId").equals(profile.getString("vaultId")))throw new Exception("Cloud settings do not match this vault.");
                long epoch=env.getLong("keyEpoch");if(profile.optLong("keyEpoch",0)==0)profile.put("keyEpoch",epoch);
                if(profile.getLong("keyEpoch")!=epoch)throw new Exception("The master passphrase changed. Reconnect cloud after reconciling your devices.");
                key=CloudSnapshot.parseKey(profile.getString("recoveryKey"));
                response=request("GET","rest/v1/opaque_device_snapshots?select=device_slot,ciphertext_sha256&vault_slot=eq."+slot+"&limit=64",null,session.access,65536);
                JSONArray metadata=StrictJson.array(new String(response,StandardCharsets.UTF_8));Arrays.fill(response,(byte)0);response=null;
                TreeMap<String,String> others=new TreeMap<>();String ownDigest="";
                if(metadata.length()>64)throw new Exception("Too many cloud devices.");
                for(int i=0;i<metadata.length();i++){JSONObject row=metadata.getJSONObject(i);String id=row.getString("device_slot"),hash=row.getString("ciphertext_sha256");if(!VaultEngine.id(id)||!hash.matches("[0-9a-f]{64}"))throw new Exception("Invalid cloud metadata.");if(id.equals(device))ownDigest=hash;else if(others.put(id,hash)!=null)throw new Exception("Duplicate cloud device.");}
                String stamp=stamp(others);byte[] cached=store.readPending();boolean missing=!profile.optString("downloadedStamp").equals(profile.optString("mergedStamp"))&&cached==null;
                if(!stamp.equals(profile.optString("downloadedStamp"))||missing){
                    response=request("GET","rest/v1/opaque_device_snapshots?select=device_slot,ciphertext_base64,ciphertext_sha256&vault_slot=eq."+slot+"&limit=64",null,session.access,16000000);
                    JSONArray rows=StrictJson.array(new String(response,StandardCharsets.UTF_8));Arrays.fill(response,(byte)0);response=null;
                    JSONArray pending=new JSONArray();others.clear();if(rows.length()>64)throw new Exception("Too many cloud devices.");
                    for(int i=0;i<rows.length();i++){
                        JSONObject row=rows.getJSONObject(i);String id=row.getString("device_slot"),digest=row.getString("ciphertext_sha256"),encoded=row.getString("ciphertext_base64");
                        if(!VaultEngine.id(id)||!digest.matches("[0-9a-f]{64}")||encoded.length()>2800000)throw new Exception("Invalid cloud backup metadata.");
                        byte[] blob=Base64.getDecoder().decode(encoded);
                        try{if(blob.length>CloudSnapshot.MAX_INNER+29||!hex(VaultEngine.sha(blob)).equals(digest))throw new Exception("Cloud backup checksum failed.");
                            if(!id.equals(device)){if(others.put(id,digest)!=null)throw new Exception("Duplicate cloud device.");byte[] inner=CloudSnapshot.open(blob,key,slot,id);
                                try{JSONObject remote=VaultEngine.parse(inner);if(!remote.getString("vaultId").equals(profile.getString("vaultId"))||remote.getLong("keyEpoch")!=epoch)throw new Exception("A device changed the vault key. Local data is preserved.");}finally{Arrays.fill(inner,(byte)0);}
                                pending.put(new JSONObject().put("deviceSlot",id).put("blob",encoded));}
                        }finally{Arrays.fill(blob,(byte)0);}
                    }
                    byte[] staged=pending.toString().getBytes(StandardCharsets.UTF_8);
                    try{check();store.writePending(staged);profile.put("downloadedStamp",stamp(others));store.save(profile);changed=true;}finally{Arrays.fill(staged,(byte)0);}
                }else if(cached!=null&&!profile.optString("downloadedStamp").equals(profile.optString("mergedStamp"))){
                    JSONArray rows=StrictJson.array(new String(cached,StandardCharsets.UTF_8));if(rows.length()>64)throw new Exception("Too many pending cloud devices.");
                    for(int i=0;i<rows.length();i++){JSONObject row=rows.getJSONObject(i);String id=row.getString("deviceSlot"),encoded=row.getString("blob");if(!VaultEngine.id(id)||encoded.length()>2800000)throw new Exception("Invalid pending cloud data.");byte[] blob=Base64.getDecoder().decode(encoded);
                        try{byte[] inner=CloudSnapshot.open(blob,key,slot,id);JSONObject remote=VaultEngine.parse(inner);if(!remote.getString("vaultId").equals(profile.getString("vaultId"))||remote.getLong("keyEpoch")!=epoch)throw new Exception("A device changed the vault key. Local data is preserved.");snapshots.add(inner);}finally{Arrays.fill(blob,(byte)0);}}
                }
                if(cached!=null)Arrays.fill(cached,(byte)0);
                if(engine!=null&&engine.isOpen()&&!profile.optString("downloadedStamp").equals(profile.optString("mergedStamp"))){
                    if(snapshots.isEmpty()){
                        byte[] staged=store.readPending();if(staged!=null){try{JSONArray rows=StrictJson.array(new String(staged,StandardCharsets.UTF_8));for(int i=0;i<rows.length();i++){JSONObject row=rows.getJSONObject(i);String id=row.getString("deviceSlot");byte[] blob=Base64.getDecoder().decode(row.getString("blob"));try{snapshots.add(CloudSnapshot.open(blob,key,slot,id));}finally{Arrays.fill(blob,(byte)0);}}}finally{Arrays.fill(staged,(byte)0);}}
                    }
                    check();conflicts=engine.mergeCloud(snapshots);Arrays.fill(local,(byte)0);local=engine.export();profile.put("mergedStamp",profile.getString("downloadedStamp"));store.save(profile);store.writePending(null);
                }
                String hash=hex(VaultEngine.sha(local));
                if(!hash.equals(profile.optString("lastUploadedHash"))||ownDigest.isEmpty()||!ownDigest.equals(profile.optString("lastOuterHash"))){
                    outer=CloudSnapshot.seal(local,key,slot,device);String outerHash=hex(VaultEngine.sha(outer));JSONObject body=new JSONObject().put("owner_id",session.owner).put("vault_slot",slot).put("device_slot",device).put("ciphertext_base64",Base64.getEncoder().encodeToString(outer)).put("ciphertext_sha256",outerHash);
                    byte[] reply=request("POST","rest/v1/opaque_device_snapshots?on_conflict=owner_id,vault_slot,device_slot",body,session.access,65536);Arrays.fill(reply,(byte)0);
                    profile.put("lastUploadedHash",hash).put("lastOuterHash",outerHash);changed=true;
                }
                Instant now=Instant.now();String previous=profile.optString("lastSyncUtc");
                try{if(previous.isEmpty()||now.toEpochMilli()-Instant.parse(previous).toEpochMilli()>60000)changed=true;}catch(Exception invalid){changed=true;}
                if(changed){profile.put("lastSyncUtc",now.toString());store.save(profile);}return conflicts;
            }finally{CloudSettingsStore.clear(latest);if(response!=null)Arrays.fill(response,(byte)0);if(key!=null)Arrays.fill(key,(byte)0);if(local!=null)Arrays.fill(local,(byte)0);if(outer!=null)Arrays.fill(outer,(byte)0);for(byte[] inner:snapshots)Arrays.fill(inner,(byte)0);}
        }
    }
    private static String stamp(SortedMap<String,String> rows)throws Exception{StringBuilder joined=new StringBuilder();for(Map.Entry<String,String> row:rows.entrySet()){if(joined.length()>0)joined.append('\n');joined.append(row.getKey()).append(':').append(row.getValue());}return hex(VaultEngine.sha(joined.toString().getBytes(StandardCharsets.UTF_8)));}
    byte[] firstSnapshot(JSONObject pairing,CloudAuth.Session session)throws Exception{
        String slot=pairing.getString("vaultSlot");byte[] key=CloudSnapshot.parseKey(pairing.getString("recoveryKey")),response=null;
        try{
            response=request("GET","rest/v1/opaque_device_snapshots?select=device_slot,ciphertext_base64,ciphertext_sha256&vault_slot=eq."+slot+"&order=updated_at.desc&limit=64",null,session.access,16000000);
            JSONArray rows=StrictJson.array(new String(response,StandardCharsets.UTF_8));if(rows.length()==0)throw new Exception("No cloud backup yet. Sync the first device before joining.");
            if(rows.length()>64)throw new Exception("Too many cloud devices.");
            JSONObject row=rows.getJSONObject(0);String id=row.getString("device_slot"),hash=row.getString("ciphertext_sha256"),encoded=row.getString("ciphertext_base64");
            if(!VaultEngine.id(id)||!hash.matches("[0-9a-f]{64}")||encoded.length()>2800000)throw new Exception("Invalid cloud backup metadata.");
            byte[] blob=Base64.getDecoder().decode(encoded);
            try{
                if(blob.length>CloudSnapshot.MAX_INNER+29||!hex(VaultEngine.sha(blob)).equals(hash))throw new Exception("Cloud backup checksum failed.");
                byte[] inner=CloudSnapshot.open(blob,key,slot,id);
                if(!VaultEngine.parse(inner).getString("vaultId").equals(pairing.getString("vaultId"))){Arrays.fill(inner,(byte)0);throw new Exception("Cloud vault identity does not match the connection code.");}
                return inner;
            }finally{Arrays.fill(blob,(byte)0);}
        }finally{Arrays.fill(key,(byte)0);if(response!=null)Arrays.fill(response,(byte)0);}
    }
    /** Ephemeral device pairing transport. Uses the signed-in account and server RLS;
     * only public keys and ciphertext ever pass through Supabase. */
    JSONObject pairingRead(DevicePairing session,CloudAuth.Session signed)throws Exception{
        byte[] body=request("GET","rest/v1/device_pairing_sessions?select=*&session_id=eq."+session.sessionId+"&limit=1",null,signed.access,8192);
        try{
            JSONArray rows=StrictJson.array(new String(body,StandardCharsets.UTF_8));
            if(rows.length()!=1)throw new Exception("This QR expired or was cancelled on the computer.");
            JSONObject row=rows.getJSONObject(0);
            if(!row.getString("owner_id").equals(signed.owner)||
               !row.getString("initiator_public_key").equals(session.initiatorKey)||
               !row.getString("session_id").equals(session.sessionId)||
               Instant.parse(row.getString("expires_at")).toEpochMilli()<=System.currentTimeMillis())
                throw new Exception("Pairing session does not match your cloud account.");
            return row;
        }finally{Arrays.fill(body,(byte)0);}
    }
    void pairingRequest(DevicePairing session,CloudAuth.Session signed)throws Exception{
        JSONObject row=pairingRead(session,signed);
        if(!row.getString("state").equals("waiting"))throw new Exception("That pairing QR has already been used.");
        byte[] response=request("PATCH","rest/v1/device_pairing_sessions?session_id=eq."+session.sessionId+"&state=eq.waiting",
            new JSONObject().put("state","requested").put("joiner_public_key",session.publicKey),signed.access,8192);
        Arrays.fill(response,(byte)0);
        JSONObject requested=pairingRead(session,signed);
        if(!requested.getString("state").equals("requested")||!requested.getString("joiner_public_key").equals(session.publicKey))
            throw new Exception("Another phone requested this QR. Start a new pairing.");
    }
    String pairingSealed(DevicePairing session,CloudAuth.Session signed)throws Exception{
        JSONObject row=pairingRead(session,signed);
        String state=row.getString("state");
        if(!row.getString("joiner_public_key").equals(session.publicKey))
            throw new Exception("Pairing recipient changed. Please restart pairing.");
        if(state.equals("requested"))return null;
        if(!state.equals("approved")&&!state.equals("completed"))
            throw new Exception("Pairing was cancelled or rejected.");
        return row.getString("sealed_pairing");
    }
    void pairingComplete(DevicePairing session,CloudAuth.Session signed)throws Exception{
        byte[] response=request("PATCH","rest/v1/device_pairing_sessions?session_id=eq."+session.sessionId+"&state=eq.approved",
            new JSONObject().put("state","completed"),signed.access,8192);
        Arrays.fill(response,(byte)0);
        if(!pairingRead(session,signed).getString("state").equals("completed"))
            throw new Exception("The pairing acknowledgement was not accepted.");
    }
    void signOut(CloudAuth.Session signedIn,CloudSettingsStore store)throws Exception{synchronized(GATE){auth.signOut(signedIn);check();store.forget();}}
    private static String hex(byte[] bytes){StringBuilder result=new StringBuilder();for(byte b:bytes)result.append(String.format(Locale.ROOT,"%02x",b&255));return result.toString();}
    @Override public void close(){cancelled=true;if(!persistingRefresh){HttpsURLConnection conn=connection;if(conn!=null)conn.disconnect();}}
}
