package com.bosha.vault;

import android.app.*;
import android.hardware.biometrics.BiometricPrompt;
import android.os.*;
import android.security.keystore.*;
import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.security.*;
import java.util.Arrays;
import javax.crypto.*;
import javax.crypto.spec.GCMParameterSpec;
import org.json.JSONObject;

final class Biometrics {
    private static final String ALIAS="BoshaVault.biometric.v1";
    interface Result { void done(byte[] key,Exception error); }
    static File file(Activity a){return new File(a.getFilesDir(),"biometric.wrap");}
    static boolean available(Activity a){return file(a).exists();}
    static void disable(Activity a)throws Exception{Files.deleteIfExists(file(a).toPath());KeyStore ks=KeyStore.getInstance("AndroidKeyStore");ks.load(null);if(ks.containsAlias(ALIAS))ks.deleteEntry(ALIAS);}
    static void enable(Activity a,byte[] secret,Result callback){
        try{
            disable(a);KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");KeyGenParameterSpec.Builder b=new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setKeySize(256).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setUserAuthenticationRequired(true).setInvalidatedByBiometricEnrollment(true);
            if(Build.VERSION.SDK_INT>=30)b.setUserAuthenticationParameters(0,KeyProperties.AUTH_BIOMETRIC_STRONG);else b.setUserAuthenticationValidityDurationSeconds(-1);
            generator.init(b.build());generator.generateKey();Cipher cipher=cipher();KeyStore ks=KeyStore.getInstance("AndroidKeyStore");ks.load(null);cipher.init(Cipher.ENCRYPT_MODE,ks.getKey(ALIAS,null));cipher.updateAAD("BoshaVault biometric key v1".getBytes(StandardCharsets.UTF_8));
            prompt(a,"Enable fingerprint unlock",cipher,(actual,error)->{
                try{if(error!=null){callback.done(null,error);return;}byte[] ct=actual.doFinal(secret);JSONObject box=new JSONObject().put("nonce",VaultEngine.b64(actual.getIV())).put("ciphertext",VaultEngine.b64(ct));Files.write(file(a).toPath(),box.toString().getBytes(StandardCharsets.UTF_8));callback.done(null,null);}
                catch(Exception e){callback.done(null,e);}finally{Arrays.fill(secret,(byte)0);}
            });
        }catch(Exception e){Arrays.fill(secret,(byte)0);callback.done(null,e);}
    }
    static void unlock(Activity a,Result callback){
        try{
            JSONObject box=StrictJson.object(VaultEngine.utf8(Files.readAllBytes(file(a).toPath())));StrictJson.keys(box,"nonce","ciphertext");KeyStore ks=KeyStore.getInstance("AndroidKeyStore");ks.load(null);Cipher cipher=cipher();cipher.init(Cipher.DECRYPT_MODE,ks.getKey(ALIAS,null),new GCMParameterSpec(128,VaultEngine.decode(box.getString("nonce"),12)));cipher.updateAAD("BoshaVault biometric key v1".getBytes(StandardCharsets.UTF_8));
            prompt(a,"Unlock your private space",cipher,(actual,error)->{try{if(error!=null){callback.done(null,error);return;}callback.done(actual.doFinal(VaultEngine.decode(box.getString("ciphertext"),48)),null);}catch(Exception e){callback.done(null,e);}});
        }catch(Exception e){callback.done(null,e);}
    }
    private static Cipher cipher()throws Exception{return Cipher.getInstance("AES/GCM/NoPadding");}
    interface CipherResult{void done(Cipher c,Exception e);}
    private static void prompt(Activity a,String title,Cipher cipher,CipherResult callback){
        BiometricPrompt.Builder b=new BiometricPrompt.Builder(a).setTitle(title).setSubtitle("Android Keystore protects your local quick-unlock key").setNegativeButton("Use passphrase",a.getMainExecutor(),(d,w)->callback.done(null,new Exception("Use your master passphrase instead.")));
        if(Build.VERSION.SDK_INT>=30)b.setAllowedAuthenticators(android.hardware.biometrics.BiometricManager.Authenticators.BIOMETRIC_STRONG);
        b.build().authenticate(new BiometricPrompt.CryptoObject(cipher),new CancellationSignal(),a.getMainExecutor(),new BiometricPrompt.AuthenticationCallback(){
            @Override public void onAuthenticationSucceeded(BiometricPrompt.AuthenticationResult r){callback.done(r.getCryptoObject().getCipher(),null);}
            @Override public void onAuthenticationError(int code,CharSequence text){callback.done(null,new Exception(text.toString()));}
        });
    }
}
