package com.bosha.vault;

import android.content.Context;
import android.security.keystore.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.KeyStore;
import java.util.*;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;
import org.json.JSONObject;

/** App-private encrypted settings. Android Keystore key is never exported or backed up. */
final class CloudSettingsStore {
    private final File path;
    private static final String ALIAS="BoshaVault.CloudSettings.v1";
    CloudSettingsStore(Context context){path=new File(context.getFilesDir(),"cloud-settings.enc");}
    private SecretKey key()throws Exception{
        KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
        if(!store.containsAlias(ALIAS)){
            KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
            generator.init(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setKeySize(256).setRandomizedEncryptionRequired(true).build());generator.generateKey();
        }
        return (SecretKey)store.getKey(ALIAS,null);
    }
    JSONObject load(String vaultId)throws Exception{
        if(!path.exists())return null;if(path.length()>65536)throw new Exception("Invalid protected cloud settings.");
        byte[] bytes=Files.readAllBytes(path.toPath()),plain=null;
        try{
            if(bytes.length<30||bytes[0]!=1)throw new Exception("Invalid protected cloud settings.");
            Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,Arrays.copyOfRange(bytes,1,13)));plain=cipher.doFinal(bytes,13,bytes.length-13);
            JSONObject profile=StrictJson.object(new String(plain,StandardCharsets.UTF_8));validate(profile,vaultId);return profile;
        }finally{Arrays.fill(bytes,(byte)0);if(plain!=null)Arrays.fill(plain,(byte)0);}
    }
    static void validate(JSONObject profile,String vaultId)throws Exception{
        if(profile.getInt("version")!=1||!profile.getString("vaultId").equals(vaultId))throw new Exception("Cloud settings do not match this vault. Reconnect this device.");
        for(String field:new String[]{"ownerId","vaultId","vaultSlot","deviceSlot"})if(!VaultEngine.id(profile.getString(field))||UUID.fromString(profile.getString(field)).equals(new UUID(0,0)))throw new Exception("Invalid cloud identity.");
        String token=profile.getString("refreshToken");if(token.length()<10||token.length()>8192||token.matches("(?s).*\\p{Cntrl}.*")||profile.getString("email").length()>320||profile.optString("lastSyncUtc").length()>50)throw new Exception("Invalid cloud settings.");
        byte[] key=CloudSnapshot.parseKey(profile.getString("recoveryKey"));Arrays.fill(key,(byte)0);
    }
    void save(JSONObject profile)throws Exception{
        validate(profile,profile.getString("vaultId"));byte[] plain=profile.toString().getBytes(StandardCharsets.UTF_8),encrypted=null;
        File temporary=new File(path+"."+UUID.randomUUID()+".tmp");
        try{
            Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key());encrypted=cipher.doFinal(plain);
            try(FileOutputStream out=new FileOutputStream(temporary)){out.write(1);out.write(cipher.getIV());out.write(encrypted);out.getFD().sync();}
            Files.move(temporary.toPath(),path.toPath(),StandardCopyOption.REPLACE_EXISTING);
        }finally{Arrays.fill(plain,(byte)0);if(encrypted!=null)Arrays.fill(encrypted,(byte)0);Files.deleteIfExists(temporary.toPath());}
    }
    void forget()throws Exception{Files.deleteIfExists(path.toPath());}
    static void clear(JSONObject profile){if(profile!=null){profile.remove("refreshToken");profile.remove("recoveryKey");}}
}
