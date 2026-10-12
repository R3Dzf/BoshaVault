package com.bosha.vault;

import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.interfaces.ECPublicKey;
import java.security.spec.ECGenParameterSpec;
import java.security.spec.X509EncodedKeySpec;
import java.util.*;
import javax.crypto.Cipher;
import javax.crypto.KeyAgreement;
import javax.crypto.Mac;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.SecretKeySpec;
import org.json.JSONObject;

/** Short-lived ECDH device pairing; the QR NEVER contains the cloud recovery key. */
final class DevicePairing implements AutoCloseable {
    static final String PREFIX="BVPAIR2:";
    final String sessionId, initiatorKey, publicKey;
    final long expiresUnix;
    private final KeyPair keys;
    private boolean closed;

    DevicePairing(String invitation)throws Exception {
        if(invitation==null||!invitation.startsWith(PREFIX)||invitation.length()>1024||invitation.length()<100)
            throw new Exception("Scan the temporary Add device QR from your computer.");
        byte[] source=decodeUrl(invitation.substring(PREFIX.length()),512);
        JSONObject j;
        try{j=StrictJson.object(new String(source,StandardCharsets.UTF_8));}
        finally{Arrays.fill(source,(byte)0);}
        if(j.getInt("version")!=2)throw new Exception("Unsupported pairing version.");
        sessionId=j.getString("sessionId");expiresUnix=j.getLong("expiresUnix");initiatorKey=j.getString("publicKey");
        if(!VaultEngine.id(sessionId)||UUID.fromString(sessionId).equals(new UUID(0,0)))throw new Exception("Invalid pairing session.");
        long now=System.currentTimeMillis()/1000;
        if(expiresUnix<now||expiresUnix>now+165)throw new Exception("Pairing QR expired. Generate a new one.");
        parseKey(initiatorKey);
        KeyPairGenerator generator=KeyPairGenerator.getInstance("EC");
        generator.initialize(new ECGenParameterSpec("secp256r1"),new SecureRandom());
        keys=generator.generateKeyPair();
        publicKey=Base64.getEncoder().encodeToString(keys.getPublic().getEncoded());
    }

    String securityCode()throws Exception{
        byte[] h=derive("SAS");
        try {long bits=ByteBuffer.wrap(h).getInt()&0xffffffffL;return String.format(Locale.ROOT,"%06d",bits%1000000L);}
        finally {Arrays.fill(h,(byte)0);}
    }

    String open(String response)throws Exception{
        check();
        if(response==null||response.length()<70||response.length()>4096)throw new Exception("Invalid encrypted pairing response.");
        byte[] combined=Base64.getDecoder().decode(response),key=derive("AES"),plain=new byte[0];
        try{
            if(combined.length<12+16+100||combined.length>2048)throw new Exception("Invalid pairing response size.");
            byte[] nonce=Arrays.copyOf(combined,12);
            Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE,new SecretKeySpec(key,"AES"),new GCMParameterSpec(128,nonce));
            cipher.updateAAD(aad());
            plain=cipher.doFinal(combined,12,combined.length-12);
            String code=new String(plain,StandardCharsets.UTF_8);
            if(!code.startsWith("BVCLOUD1:")||code.length()>1024)throw new Exception("Invalid vault pairing payload.");
            return code;
        }catch(GeneralSecurityException invalid){
            throw new Exception("Device pairing authentication failed; restart pairing.",invalid);
        }finally{Arrays.fill(key,(byte)0);Arrays.fill(combined,(byte)0);Arrays.fill(plain,(byte)0);}
    }

    private byte[] derive(String purpose)throws Exception{
        check();
        PublicKey peer=parseKey(initiatorKey);
        KeyAgreement dh=KeyAgreement.getInstance("ECDH");dh.init(keys.getPrivate());dh.doPhase(peer,true);
        byte[] shared=dh.generateSecret(),salt=MessageDigest.getInstance("SHA-256").digest(aad());
        try{
            byte[] prk=hmac(salt,shared);
            try{return hmac(prk,purpose.getBytes(StandardCharsets.US_ASCII));}
            finally{Arrays.fill(prk,(byte)0);}
        }finally{Arrays.fill(shared,(byte)0);Arrays.fill(salt,(byte)0);}
    }

    private byte[] aad(){
        return ("Lockvyn.Pairing.v2|"+sessionId+"|"+initiatorKey+"|"+publicKey).getBytes(StandardCharsets.UTF_8);
    }
    private static byte[] hmac(byte[] key,byte[] input)throws Exception{
        Mac mac=Mac.getInstance("HmacSHA256");mac.init(new SecretKeySpec(key,"HmacSHA256"));return mac.doFinal(input);
    }
    private static PublicKey parseKey(String text)throws Exception{
        if(text==null||text.length()<110||text.length()>180)throw new Exception("Invalid pairing public key.");
        byte[] der=Base64.getDecoder().decode(text);
        try{
            PublicKey result=KeyFactory.getInstance("EC").generatePublic(new X509EncodedKeySpec(der));
            if(!(result instanceof ECPublicKey)||((ECPublicKey)result).getParams().getCurve().getField().getFieldSize()!=256||
               !((ECPublicKey)result).getParams().getOrder().equals(new java.math.BigInteger("115792089210356248762697446949407573529996955224135760342422259061068512044369")))
                throw new Exception("Device pairing requires NIST P-256.");
            return result;
        }finally{Arrays.fill(der,(byte)0);}
    }
    private static byte[] decodeUrl(String text,int max)throws Exception{
        if(text.length()>max*2||!text.matches("[A-Za-z0-9_-]+"))throw new Exception("Invalid pairing QR.");
        byte[] bytes=Base64.getUrlDecoder().decode(text);
        if(bytes.length>max){Arrays.fill(bytes,(byte)0);throw new Exception("Pairing QR too long.");}
        return bytes;
    }
    private void check()throws Exception{
        if(closed)throw new Exception("Pairing has been closed.");
        if(System.currentTimeMillis()/1000>=expiresUnix)throw new Exception("Pairing expired. Start again.");
    }
    @Override public void close(){closed=true;}
}
