package com.bosha.vault;

import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.spec.ECGenParameterSpec;
import java.util.*;
import javax.crypto.Cipher;
import javax.crypto.KeyAgreement;
import javax.crypto.Mac;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.SecretKeySpec;
import org.json.JSONObject;

/** End-to-end synthetic pairing crypto tests (no real vault or cloud account). */
public final class DevicePairingTest {
    private static void require(boolean ok,String message){if(!ok)throw new AssertionError(message);}
    private static byte[] hmac(byte[] key,byte[] data)throws Exception{
        Mac h=Mac.getInstance("HmacSHA256");h.init(new SecretKeySpec(key,"HmacSHA256"));return h.doFinal(data);
    }
    private static byte[] key(byte[] shared,byte[] aad,String purpose)throws Exception{
        byte[] salt=MessageDigest.getInstance("SHA-256").digest(aad);
        byte[] prk=hmac(salt,shared);
        try{return hmac(prk,purpose.getBytes(StandardCharsets.US_ASCII));}
        finally{Arrays.fill(salt,(byte)0);Arrays.fill(prk,(byte)0);}
    }
    public static void main(String[] args)throws Exception{
        KeyPairGenerator g=KeyPairGenerator.getInstance("EC");
        g.initialize(new ECGenParameterSpec("secp256r1"),new SecureRandom());
        KeyPair pc=g.generateKeyPair();
        String issuer=Base64.getEncoder().encodeToString(pc.getPublic().getEncoded());
        UUID session=UUID.randomUUID();
        long expiry=System.currentTimeMillis()/1000+120;
        JSONObject invite=new JSONObject().put("version",2).put("sessionId",session.toString())
            .put("expiresUnix",expiry).put("publicKey",issuer);
        String code=DevicePairing.PREFIX+Base64.getUrlEncoder().withoutPadding()
            .encodeToString(invite.toString().getBytes(StandardCharsets.UTF_8));
        try(DevicePairing phone=new DevicePairing(code)){
            require(!code.contains("BVCLOUD1:"),"No vault key in invitation");
            KeyAgreement agreement=KeyAgreement.getInstance("ECDH");
            agreement.init(pc.getPrivate());agreement.doPhase(KeyFactory.getInstance("EC").generatePublic(
                new java.security.spec.X509EncodedKeySpec(Base64.getDecoder().decode(phone.publicKey))),true);
            byte[] shared=agreement.generateSecret();
            byte[] aad=("Lockvyn.Pairing.v2|"+session+"|"+issuer+"|"+phone.publicKey).getBytes(StandardCharsets.UTF_8);
            byte[] sasKey=key(shared,aad,"SAS");
            String expected=String.format(Locale.ROOT,"%06d",(ByteBuffer.wrap(sasKey).getInt()&0xffffffffL)%1000000L);
            require(expected.equals(phone.securityCode()),"SAS matches the initiator's ECDH secret");
            byte[] aes=key(shared,aad,"AES"),nonce=new byte[12];new SecureRandom().nextBytes(nonce);
            String payload="BVCLOUD1:"+Base64.getUrlEncoder().withoutPadding().encodeToString(
                new byte[192]);
            Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE,new SecretKeySpec(aes,"AES"),new GCMParameterSpec(128,nonce));
            cipher.updateAAD(aad);
            byte[] sealed=cipher.doFinal(payload.getBytes(StandardCharsets.UTF_8));
            byte[] combined=new byte[nonce.length+sealed.length];System.arraycopy(nonce,0,combined,0,12);
            System.arraycopy(sealed,0,combined,12,sealed.length);
            String opaque=Base64.getEncoder().encodeToString(combined);
            require(phone.open(opaque).equals(payload),"Device decrypts approved authenticated pairing envelope");
            combined[15]^=1;boolean rejected=false;
            try{phone.open(Base64.getEncoder().encodeToString(combined));}catch(Exception e){rejected=true;}
            require(rejected,"Tampered encrypted pairing envelope rejected");
            Arrays.fill(shared,(byte)0);Arrays.fill(aes,(byte)0);Arrays.fill(sasKey,(byte)0);
            Arrays.fill(sealed,(byte)0);Arrays.fill(combined,(byte)0);
        }
        JSONObject stale=new JSONObject().put("version",2).put("sessionId",UUID.randomUUID().toString())
            .put("expiresUnix",System.currentTimeMillis()/1000-60).put("publicKey",issuer);
        boolean expired=false;try(DevicePairing ignored=new DevicePairing(
            DevicePairing.PREFIX+Base64.getUrlEncoder().withoutPadding().encodeToString(
            stale.toString().getBytes(StandardCharsets.UTF_8)))){}
        catch(Exception e){expired=true;}
        require(expired,"Expired invitation rejected");
        System.out.println("PASS temporary public-only QR, ECDH SAS, AES-GCM authenticated response, tampering and expiry");
    }
}
