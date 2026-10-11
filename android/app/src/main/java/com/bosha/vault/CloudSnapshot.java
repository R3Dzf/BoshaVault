package com.bosha.vault;

import java.nio.charset.StandardCharsets;
import java.security.SecureRandom;
import java.util.*;
import javax.crypto.Cipher;
import javax.crypto.spec.*;
import org.json.JSONObject;

/** Binary format shared with CloudSnapshotCodec.cs; keys never enter HTTP requests. */
public final class CloudSnapshot {
    public static final int MAX_INNER=2000000;
    public static String newKey(){byte[] key=new byte[32];new SecureRandom().nextBytes(key);try{return Base64.getUrlEncoder().withoutPadding().encodeToString(key);}finally{Arrays.fill(key,(byte)0);}}
    public static byte[] parseKey(String code)throws Exception{
        if(code==null||!code.matches("[A-Za-z0-9_-]{43}"))throw new Exception("Invalid cloud recovery key.");
        byte[] key=Base64.getUrlDecoder().decode(code);if(key.length!=32)throw new Exception("Invalid cloud recovery key.");return key;
    }
    private static byte[] aad(String vault,String device)throws Exception{
        if(!VaultEngine.id(vault)||!VaultEngine.id(device)||UUID.fromString(vault).equals(new UUID(0,0))||UUID.fromString(device).equals(new UUID(0,0)))throw new Exception("Invalid cloud device identity.");
        return ("BoshaVault.CloudOuter.v1|"+vault.toLowerCase(Locale.ROOT)+"|"+device.toLowerCase(Locale.ROOT)).getBytes(StandardCharsets.UTF_8);
    }
    public static byte[] seal(byte[] inner,byte[] key,String vault,String device)throws Exception{
        if(key.length!=32||inner.length<100||inner.length>MAX_INNER)throw new Exception("Vault exceeds the cloud snapshot limit.");
        VaultEngine.parse(inner);byte[] nonce=new byte[12];new SecureRandom().nextBytes(nonce);byte[] associated=aad(vault,device),sealed=null;
        try{
            Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,new SecretKeySpec(key,"AES"),new GCMParameterSpec(128,nonce));cipher.updateAAD(associated);sealed=cipher.doFinal(inner);
            byte[] out=new byte[29+inner.length];out[0]=1;System.arraycopy(nonce,0,out,1,12);System.arraycopy(sealed,sealed.length-16,out,13,16);System.arraycopy(sealed,0,out,29,inner.length);return out;
        }finally{Arrays.fill(associated,(byte)0);if(sealed!=null)Arrays.fill(sealed,(byte)0);}
    }
    public static byte[] open(byte[] outer,byte[] key,String vault,String device)throws Exception{
        if(key.length!=32||outer.length<129||outer.length>MAX_INNER+29||outer[0]!=1)throw new Exception("Invalid cloud snapshot.");
        byte[] associated=aad(vault,device),combined=new byte[outer.length-13],inner=null;
        try{
            System.arraycopy(outer,29,combined,0,outer.length-29);System.arraycopy(outer,13,combined,outer.length-29,16);
            Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,new SecretKeySpec(key,"AES"),new GCMParameterSpec(128,Arrays.copyOfRange(outer,1,13)));cipher.updateAAD(associated);inner=cipher.doFinal(combined);VaultEngine.parse(inner);return inner;
        }catch(Exception e){if(inner!=null)Arrays.fill(inner,(byte)0);throw new Exception("Cloud backup authentication failed. Check your connection code and vault.");}
        finally{Arrays.fill(associated,(byte)0);Arrays.fill(combined,(byte)0);}
    }
    public static String pairing(JSONObject profile)throws Exception{
        JSONObject data=new JSONObject().put("version",1).put("ownerId",profile.getString("ownerId")).put("vaultId",profile.getString("vaultId")).put("vaultSlot",profile.getString("vaultSlot")).put("recoveryKey",profile.getString("recoveryKey"));
        byte[] bytes=data.toString().getBytes(StandardCharsets.UTF_8);try{return "BVCLOUD1:"+Base64.getUrlEncoder().withoutPadding().encodeToString(bytes);}finally{Arrays.fill(bytes,(byte)0);}
    }
    public static JSONObject pairing(String code,String vaultId,String ownerId)throws Exception{
        if(code==null||code.length()>1024||!code.startsWith("BVCLOUD1:")||!code.substring(9).matches("[A-Za-z0-9_-]+"))throw new Exception("Paste a cloud connection code from your other device.");
        byte[] data=Base64.getUrlDecoder().decode(code.substring(9));
        try{
            JSONObject result=StrictJson.object(new String(data,StandardCharsets.UTF_8));StrictJson.keys(result,"version","ownerId","vaultId","vaultSlot","recoveryKey");
            if(StrictJson.number(result,"version")!=1||!result.getString("ownerId").equalsIgnoreCase(ownerId)||!result.getString("vaultId").equalsIgnoreCase(vaultId)||!VaultEngine.id(result.getString("vaultSlot"))||UUID.fromString(result.getString("vaultSlot")).equals(new UUID(0,0)))throw new Exception("Use the same cloud account and vault on both devices. Transfer the same vault over Wi-Fi or import its encrypted backup first.");
            byte[] key=parseKey(result.getString("recoveryKey"));Arrays.fill(key,(byte)0);return result;
        }finally{Arrays.fill(data,(byte)0);}
    }
}
