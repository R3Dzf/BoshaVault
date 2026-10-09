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
    private static final String CBC_ALIAS="BoshaVault.biometric.cbc.v2";
    interface Result { void done(byte[] key,Exception error); }
    static File file(Activity a){return new File(a.getFilesDir(),"biometric.wrap");}
    static boolean available(Activity a){return file(a).exists();}
    static void disable(Activity a)throws Exception{Files.deleteIfExists(file(a).toPath());KeyStore ks=KeyStore.getInstance("AndroidKeyStore");ks.load(null);if(ks.containsAlias(ALIAS))ks.deleteEntry(ALIAS);if(ks.containsAlias(CBC_ALIAS))ks.deleteEntry(CBC_ALIAS);}
    // CryptoObject decryption requires a strong biometric authenticator; a device
    // may unlock its screen with a weaker fingerprint but be unable to protect keys.
    static void verifySupport(Activity a) throws Exception {
        android.app.KeyguardManager guard=(android.app.KeyguardManager)a.getSystemService(Activity.KEYGUARD_SERVICE);
        if(guard==null || !guard.isDeviceSecure())throw new Exception("Set up a secure screen lock (PIN, pattern or password) in Android Settings first.");
        if(Build.VERSION.SDK_INT>=30){
            android.hardware.biometrics.BiometricManager manager=a.getSystemService(android.hardware.biometrics.BiometricManager.class);
            if(manager==null)throw new Exception("Android biometric service is unavailable on this device.");
            int status=manager.canAuthenticate(android.hardware.biometrics.BiometricManager.Authenticators.BIOMETRIC_STRONG);
            if(status==android.hardware.biometrics.BiometricManager.BIOMETRIC_SUCCESS)return;
            if(status==android.hardware.biometrics.BiometricManager.BIOMETRIC_ERROR_NONE_ENROLLED)
                throw new Exception("Enroll a strong fingerprint or other strong biometric in Android Settings before enabling quick unlock.");
            if(status==android.hardware.biometrics.BiometricManager.BIOMETRIC_ERROR_HW_UNAVAILABLE)
                throw new Exception("The biometric sensor is temporarily unavailable. Try again after unlocking the phone.");
            if(status==android.hardware.biometrics.BiometricManager.BIOMETRIC_ERROR_NO_HARDWARE)
                throw new Exception("No Android-approved strong biometric sensor is available. Continue using your master passphrase.");
            throw new Exception("This phone cannot authenticate with BIOMETRIC_STRONG (Android status "+status+"). Your master passphrase still works.");
        }
        if(Build.VERSION.SDK_INT==29){
            android.hardware.biometrics.BiometricManager manager=a.getSystemService(android.hardware.biometrics.BiometricManager.class);
            if(manager==null || manager.canAuthenticate()!=android.hardware.biometrics.BiometricManager.BIOMETRIC_SUCCESS)
                throw new Exception("Enroll a supported biometric in Android Settings and try again.");
        } else {
            android.hardware.fingerprint.FingerprintManager manager=(android.hardware.fingerprint.FingerprintManager)a.getSystemService(Activity.FINGERPRINT_SERVICE);
            if(manager==null || !manager.isHardwareDetected() || !manager.hasEnrolledFingerprints())
                throw new Exception("This phone has no enrolled hardware fingerprint available for secure quick unlock.");
        }
    }
    private static Exception stepError(String step,Exception e) {
        StringBuilder message=new StringBuilder(step);
        Throwable t=e;
        int depth=0;
        while(t!=null && depth<4){
            message.append(depth==0?" (":"; cause ");
            message.append(t.getClass().getSimpleName());
            String detail=t.getMessage();
            if(detail!=null&&!detail.trim().isEmpty()){
                detail=detail.replace('\\n',' ').replace('\\r',' ');
                message.append(": ").append(detail.substring(0,Math.min(110,detail.length())));
            }
            if(depth==0)message.append(")");
            t=t.getCause();
            depth++;
        }
        return new Exception(message.toString(),e);
    }
    static void enable(Activity a,byte[] secret,Result callback){
        String stage="Biometric support check";
        try{
            verifySupport(a);
            stage="Android Keystore key creation";
            disable(a);KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");KeyGenParameterSpec.Builder b=new KeyGenParameterSpec.Builder(CBC_ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setKeySize(256).setBlockModes(KeyProperties.BLOCK_MODE_CBC).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_PKCS7).setUserAuthenticationRequired(true).setInvalidatedByBiometricEnrollment(true);
            if(Build.VERSION.SDK_INT>=30)b.setUserAuthenticationParameters(0,KeyProperties.AUTH_BIOMETRIC_STRONG);else b.setUserAuthenticationValidityDurationSeconds(-1);
            generator.init(b.build());generator.generateKey();Cipher cipher=Cipher.getInstance("AES/CBC/PKCS7Padding");KeyStore ks=KeyStore.getInstance("AndroidKeyStore");ks.load(null);cipher.init(Cipher.ENCRYPT_MODE,ks.getKey(CBC_ALIAS,null));
            final byte[] nonce=cipher.getIV();
            if(nonce==null || nonce.length!=16)throw new Exception("Invalid CBC initialization vector.");
            stage="Biometric prompt initialization";
            prompt(a,"Enable fingerprint unlock",cipher,(actual,error)->{
                try{
                    if(error!=null){callback.done(null,error);return;}
                    byte[] ct=actual.doFinal(secret);
                    if(ct.length!=48)throw new Exception("Invalid CBC-wrapped key length.");
                    JSONObject box=new JSONObject().put("mode","cbc-v2").put("iv",VaultEngine.b64(nonce)).put("ciphertext",VaultEngine.b64(ct));
                    File wrap=file(a),tmp=new File(a.getFilesDir(),"biometric.wrap.tmp");
                    try{
                        Files.write(tmp.toPath(),box.toString().getBytes(StandardCharsets.UTF_8));
                        Files.move(tmp.toPath(),wrap.toPath(),java.nio.file.StandardCopyOption.ATOMIC_MOVE,java.nio.file.StandardCopyOption.REPLACE_EXISTING);
                    }finally{Files.deleteIfExists(tmp.toPath());}
                    callback.done(null,null);
                }catch(Exception e){callback.done(null,stepError("Saving fingerprint quick unlock failed",e));}
                finally{Arrays.fill(secret,(byte)0);}
            });
        }catch(Exception e){Arrays.fill(secret,(byte)0);
            callback.done(null,stage.equals("Biometric support check")?e:stepError(stage+" failed",e));
        }
    }
    static void unlock(Activity a,Result callback){
        try{
            JSONObject box=StrictJson.object(VaultEngine.utf8(Files.readAllBytes(file(a).toPath())));
            boolean cbc=box.optString("mode","").equals("cbc-v2");
            if(cbc)StrictJson.keys(box,"mode","iv","ciphertext");
            else StrictJson.keys(box,"nonce","ciphertext");
            KeyStore ks=KeyStore.getInstance("AndroidKeyStore");ks.load(null);
            Cipher cipher;
            byte[] wrapped=VaultEngine.decode(box.getString("ciphertext"),48);
            if(cbc){
                cipher=Cipher.getInstance("AES/CBC/PKCS7Padding");
                cipher.init(Cipher.DECRYPT_MODE,ks.getKey(CBC_ALIAS,null),
                        new javax.crypto.spec.IvParameterSpec(VaultEngine.decode(box.getString("iv"),16)));
            }else{
                cipher=cipher();
                cipher.init(Cipher.DECRYPT_MODE,ks.getKey(ALIAS,null),
                        new GCMParameterSpec(128,VaultEngine.decode(box.getString("nonce"),12)));
                cipher.updateAAD("BoshaVault biometric key v1".getBytes(StandardCharsets.UTF_8));
            }
            prompt(a,"Unlock your private space",cipher,(actual,error)->{
                try{
                    if(error!=null){callback.done(null,error);return;}
                    if(actual==null)throw new Exception("Android returned no authenticated cipher.");
                    byte[] key=actual.doFinal(wrapped);
                    if(key.length!=32){Arrays.fill(key,(byte)0);throw new Exception("Biometric quick-unlock key has invalid length.");}
                    // The full vault AES-GCM tag is checked by openWithKey before it opens.
                    callback.done(key,null);
                }catch(Exception e){callback.done(null,stepError("Fingerprint quick unlock failed",e));}
                finally{Arrays.fill(wrapped,(byte)0);}
            });
        }catch(Exception e){callback.done(null,stepError("Preparing fingerprint unlock failed",e));}
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
