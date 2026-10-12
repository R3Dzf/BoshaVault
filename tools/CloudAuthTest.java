package com.bosha.vault;

import java.nio.charset.StandardCharsets;
import java.util.*;
import org.json.*;

/** Fake Auth transport only: no email, network, real secrets or Android storage. */
public final class CloudAuthTest {
    static int passed;static void check(boolean good,String name)throws Exception{if(!good)throw new Exception("FAIL "+name);passed++;System.out.println("PASS "+name);}
    interface Work{void run()throws Exception;}
    static void reject(Work work,String name)throws Exception{boolean rejected=false;try{work.run();}catch(Exception e){rejected=true;}check(rejected,name);}
    static String jwt(String owner,String aal)throws Exception{return "eyJhbGciOiJIUzI1NiJ9."+Base64.getUrlEncoder().withoutPadding().encodeToString(new JSONObject().put("sub",owner).put("aal",aal).toString().getBytes(StandardCharsets.UTF_8))+".synthetic-signature";}
    static final class Fake implements CloudAuth.Transport {
        final String owner=UUID.randomUUID().toString(),factor=UUID.randomUUID().toString(),challenge=UUID.randomUUID().toString();
        boolean verified,wrongOwner,duplicate,noUpgrade;String type="totp",secret="JBSWY3DPEHPK3PXP",lastBody="",lastMethod="",lastPath="";int calls;
        final List<String> paths=new ArrayList<>();
        public byte[] request(String method,String path,JSONObject body,String access)throws Exception{
            calls++;lastMethod=method;lastPath=path;paths.add(method+" "+path);lastBody=body==null?"":body.toString();
            if((path.contains("factors")||path.endsWith("/user"))&&access==null)throw new Exception("Missing bearer");
            String id=wrongOwner?UUID.randomUUID().toString():owner;JSONObject result;
            if(path.endsWith("/user")){
                JSONObject f=new JSONObject().put("id",factor).put("factor_type",type).put("friendly_name","Synthetic authenticator").put("status",verified?"verified":"unverified");
                JSONArray all=new JSONArray().put(f);if(duplicate)all.put(f);result=new JSONObject().put("id",id).put("factors",all);
            }else if(path.endsWith("/factors"))result=new JSONObject().put("id",factor).put("type","totp").put("totp",new JSONObject().put("secret",secret).put("uri","https://untrusted.invalid/").put("qr_code","<svg>untrusted</svg>"));
            else if(path.endsWith("/challenge"))result=new JSONObject().put("id",challenge);
            else {String aal=path.contains("factors/")&&path.endsWith("/verify")&&!noUpgrade?"aal2":"aal1";result=new JSONObject().put("user",new JSONObject().put("id",id)).put("access_token",jwt(id,aal)).put("refresh_token",aal.equals("aal2")?"rotated-synthetic-refresh-token":"synthetic-refresh-token-0123456789");}
            return result.toString().getBytes(StandardCharsets.UTF_8);
        }
    }
    public static void main(String[] args)throws Exception{
        Fake f=new Fake();CloudAuth auth=new CloudAuth(f);CloudAuth.Session session=auth.signIn("fake@example.invalid","synthetic password");
        check(!session.hasSecondFactor(),"Java password session stays aal1");
        check(!auth.factors(session).requiresChallenge(session),"Java unverified enrollment is not active MFA");
        f.verified=true;check(auth.factors(session).requiresChallenge(session),"Java verified TOTP requires challenge");
        f.type="phone";check(auth.factors(session).requiresChallenge(session)&&auth.factors(session).totp.isEmpty(),"Java unsupported factor cannot bypass MFA");f.type="totp";
        f.duplicate=true;reject(()->auth.factors(session),"Java duplicate factor refused");f.duplicate=false;
        f.wrongOwner=true;reject(()->auth.factors(session),"Java factor owner binding");f.wrongOwner=false;
        int before=f.calls;reject(()->auth.verify(session,f.factor,"١٢٣٤٥٦"),"Java non-ASCII OTP refused");check(f.calls==before,"Java invalid OTP makes no request");
        CloudAuth.Session upgraded=auth.verify(session,f.factor,"123456");
        check(upgraded.hasSecondFactor()&&!upgraded.refresh.equals(session.refresh),"Java MFA rotates tokens and upgrades assurance");
        check(upgraded.usableForSeconds(45),"AAL2 synthetic session is eligible for short-lived in-memory reuse");
        check(new JSONObject(f.lastBody).getString("challenge_id").equals(f.challenge),"Java verification binds server challenge");
        check(!auth.factors(upgraded).requiresChallenge(upgraded),"Java AAL2 session accepted without repeated challenge");
        f.noUpgrade=true;reject(()->auth.verify(session,f.factor,"123456"),"Java missing assurance upgrade refused");f.noUpgrade=false;
        f.wrongOwner=true;reject(()->auth.verify(session,f.factor,"123456"),"Java verification owner change refused");f.wrongOwner=false;
        reject(()->auth.enroll(session,"fake@example.invalid"),"Java factor addition requires existing MFA");
        f.verified=false;CloudAuth.Enrollment enrollment=auth.enroll(session,"fake@example.invalid");
        check(enrollment.uri.startsWith("otpauth://totp/")&&enrollment.uri.contains("issuer=Lockvyn")&&!enrollment.uri.contains("untrusted"),"Java QR URI uses validated local assembly");
        check(f.paths.contains("DELETE auth/v1/factors/"+f.factor),"Java pending enrollment removed before new setup");
        enrollment.close();check(enrollment.secret.isEmpty()&&enrollment.uri.isEmpty(),"Java closed setup releases secret references");
        f.secret="<bad>";reject(()->auth.enroll(session,"fake@example.invalid"),"Java malformed enrollment secret refused");
        auth.verifyEmail("fake@example.invalid","123456");check(new JSONObject(f.lastBody).getString("type").equals("signup")&&!new JSONObject(f.lastBody).has("password"),"Java signup confirmation code without password");
        auth.resendConfirmation("fake@example.invalid");check(new JSONObject(f.lastBody).getString("code_challenge_method").equals("s256")&&!new JSONObject(f.lastBody).has("code_verifier"),"Java confirmation resend keeps PKCE verifier private");
        auth.unenroll(upgraded,f.factor);check(f.lastMethod.equals("DELETE"),"Java authenticated factor removal");
        check(!CloudAuth.error(400,"auth/v1/factors/verify","mfa_verification_failed").contains("private"),"Java errors use controlled messages");
        System.out.println("RESULT: "+passed+" Java MFA/email-code checks passed.");
    }
}
