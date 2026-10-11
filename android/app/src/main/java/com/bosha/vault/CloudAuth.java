package com.bosha.vault;

import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.util.*;
import org.json.*;

/** Auth transport is separate from Android storage so both protocols are tested. */
final class CloudAuth {
    interface Transport { byte[] request(String method,String path,JSONObject body,String access)throws Exception; }
    private final Transport transport;
    CloudAuth(Transport transport){this.transport=transport;}
    private byte[] post(String path,JSONObject body,String token)throws Exception{return transport.request("POST","auth/v1/"+path,body,token);}
    static final class Session {
        final String owner,access,refresh,assurance;
        Session(byte[] bytes)throws Exception{
            try{
                JSONObject j=StrictJson.object(new String(bytes,StandardCharsets.UTF_8));owner=j.getJSONObject("user").getString("id");access=j.getString("access_token");refresh=j.getString("refresh_token");
                if(!VaultEngine.id(owner)||UUID.fromString(owner).equals(new UUID(0,0))||access.length()<20||access.length()>8192||refresh.length()<10||refresh.length()>8192||access.matches("(?s).*\\p{Cntrl}.*")||refresh.matches("(?s).*\\p{Cntrl}.*"))throw new Exception();
                String[] parts=access.split("\\.");if(parts.length!=3)throw new Exception();
                byte[] claims=Base64.getUrlDecoder().decode(parts[1]);
                try{JSONObject jwt=StrictJson.object(new String(claims,StandardCharsets.UTF_8));if(!owner.equals(jwt.getString("sub")))throw new Exception();assurance=jwt.optString("aal","aal1");if(!assurance.equals("aal1")&&!assurance.equals("aal2"))throw new Exception();}
                finally{Arrays.fill(claims,(byte)0);}
            }catch(Exception invalid){throw new Exception("Cloud account returned an invalid session.");}
            finally{Arrays.fill(bytes,(byte)0);}
        }
        boolean hasSecondFactor(){return assurance.equals("aal2");}
    }
    static final class Factor {
        final String id,name,type;final boolean verified;
        Factor(JSONObject j)throws Exception{
            id=j.getString("id");type=j.getString("factor_type");String status=j.getString("status");
            if(!VaultEngine.id(id)||UUID.fromString(id).equals(new UUID(0,0))||type.length()>30||(!status.equals("verified")&&!status.equals("unverified")))throw new Exception();
            verified=status.equals("verified");String friendly=j.optString("friendly_name","");
            name=friendly.isEmpty()||friendly.length()>120||friendly.matches("(?s).*\\p{Cntrl}.*")?"Authenticator "+id.substring(0,8):friendly;
        }
        @Override public String toString(){return name;}
    }
    static final class Factors {
        final List<Factor> all=new ArrayList<>(),totp=new ArrayList<>();boolean hasVerified;
        boolean requiresChallenge(Session session){return hasVerified&&!session.hasSecondFactor();}
    }
    static final class Enrollment implements AutoCloseable {
        final String id;String secret,uri;
        Enrollment(String id,String secret,String uri){this.id=id;this.secret=secret;this.uri=uri;}
        @Override public void close(){secret="";uri="";}
    }
    Session signIn(String email,String password)throws Exception{
        email(email);if(password.length()<1||password.length()>1024)throw new Exception("Enter your cloud account email and password.");
        return new Session(post("token?grant_type=password",new JSONObject().put("email",email).put("password",password),null));
    }
    Session refresh(String token)throws Exception{
        if(token.length()<10||token.length()>8192)throw new Exception("Sign in to cloud again.");
        return new Session(post("token?grant_type=refresh_token",new JSONObject().put("refresh_token",token),null));
    }
    void signUp(String email,String password)throws Exception{
        email(email);if(password.length()<16||password.length()>1024)throw new Exception("Use a separate cloud account password of at least 16 characters.");
        JSONObject body=new JSONObject().put("email",email).put("password",password);pkce(body);discard(post("signup",body,null));
    }
    void resendConfirmation(String email)throws Exception{email(email);JSONObject body=new JSONObject().put("type","signup").put("email",email);pkce(body);discard(post("resend",body,null));}
    void verifyEmail(String email,String code)throws Exception{
        email(email);if(!code.matches("[0-9]{6,10}"))throw new Exception("Enter the email confirmation code (6–10 digits).");
        // Any tokens returned by email verification are discarded; MFA login follows separately.
        discard(post("verify",new JSONObject().put("email",email).put("token",code).put("type","signup"),null));
    }
    void signOut(Session session)throws Exception{discard(post("logout?scope=local",new JSONObject(),session.access));}
    Factors factors(Session session)throws Exception{
        byte[] bytes=transport.request("GET","auth/v1/user",null,session.access);
        try{
            JSONObject user=StrictJson.object(new String(bytes,StandardCharsets.UTF_8));if(!session.owner.equals(user.getString("id")))throw new Exception();
            Factors result=new Factors();JSONArray all=user.has("factors")?user.getJSONArray("factors"):new JSONArray();if(all.length()>20)throw new Exception();Set<String> ids=new HashSet<>();
            for(int i=0;i<all.length();i++){Factor f=new Factor(all.getJSONObject(i));if(!ids.add(f.id))throw new Exception();result.all.add(f);if(f.verified){result.hasVerified=true;if(f.type.equals("totp"))result.totp.add(f);}}
            return result;
        }catch(Exception invalid){throw new Exception("Cloud returned invalid authenticator details.");}finally{discard(bytes);}
    }
    Enrollment enroll(Session session,String email)throws Exception{
        email(email);Factors factors=factors(session);if(factors.requiresChallenge(session))throw new Exception("Verify your current authenticator before adding another one.");
        for(Factor f:factors.all)if(!f.verified&&f.type.equals("totp"))unenroll(session,f.id);
        byte[] bytes=post("factors",new JSONObject().put("factor_type","totp").put("issuer","Lockvyn").put("friendly_name","Lockvyn "+UUID.randomUUID().toString().substring(0,8)),session.access);
        try{
            JSONObject j=StrictJson.object(new String(bytes,StandardCharsets.UTF_8));String id=j.getString("id"),secret=j.getJSONObject("totp").getString("secret");
            if(!VaultEngine.id(id)||UUID.fromString(id).equals(new UUID(0,0))||!j.getString("type").equals("totp")||!secret.matches("[A-Z2-7]{16,128}"))throw new Exception();
            String uri="otpauth://totp/"+URLEncoder.encode("Lockvyn:"+email,"UTF-8").replace("+","%20")+"?secret="+secret+"&issuer=Lockvyn&algorithm=SHA1&digits=6&period=30";
            return new Enrollment(id,secret,uri);
        }catch(Exception invalid){throw new Exception("Cloud returned invalid authenticator setup.");}finally{discard(bytes);}
    }
    Session verify(Session session,String factorId,String code)throws Exception{
        id(factorId);if(!code.matches("[0-9]{6}"))throw new Exception("Enter the six-digit code from your authenticator.");
        byte[] bytes=post("factors/"+factorId+"/challenge",new JSONObject(),session.access);String challenge;
        try{challenge=StrictJson.object(new String(bytes,StandardCharsets.UTF_8)).getString("id");id(challenge);}catch(Exception e){throw new Exception("Cloud returned an invalid authenticator challenge.");}finally{discard(bytes);}
        Session next=new Session(post("factors/"+factorId+"/verify",new JSONObject().put("challenge_id",challenge).put("code",code),session.access));
        if(!next.owner.equals(session.owner)||!next.hasSecondFactor())throw new Exception("Two-factor verification did not return the expected account and assurance level.");return next;
    }
    void unenroll(Session session,String factorId)throws Exception{id(factorId);discard(transport.request("DELETE","auth/v1/factors/"+factorId,null,session.access));}
    private static void id(String id)throws Exception{if(!VaultEngine.id(id)||UUID.fromString(id).equals(new UUID(0,0)))throw new Exception("Choose a valid authenticator.");}
    private static void email(String email)throws Exception{if(email.length()<3||email.length()>320||!email.contains("@")||email.matches("(?s).*\\p{Cntrl}.*"))throw new Exception("Enter your cloud account email.");}
    private static void pkce(JSONObject body)throws Exception{
        byte[] verifier=new byte[32];new SecureRandom().nextBytes(verifier);
        try{String encoded=Base64.getUrlEncoder().withoutPadding().encodeToString(verifier);String challenge=Base64.getUrlEncoder().withoutPadding().encodeToString(MessageDigest.getInstance("SHA-256").digest(encoded.getBytes(StandardCharsets.US_ASCII)));body.put("code_challenge",challenge).put("code_challenge_method","s256");}finally{discard(verifier);}
    }
    private static void discard(byte[] bytes){Arrays.fill(bytes,(byte)0);}
    static String error(int status,String path,String code){
        if(status==429)return "Cloud rate limit reached. Wait before trying again.";
        if(code.equals("email_address_not_authorized"))return "Email verification delivery is not enabled for this address yet. The app owner needs to configure the email sender. Your vault works offline without an account.";
        if(code.equals("email_not_confirmed"))return "Confirm your account email first. Use Resend confirmation email if needed.";
        if(code.equals("mfa_verification_failed")||code.equals("mfa_challenge_expired"))return "The authenticator code is incorrect or expired. Use the current six-digit code and check your device clock.";
        if(code.equals("insufficient_aal")||code.startsWith("mfa_"))return "Two-factor authentication could not be completed. Check your authenticator or reopen cloud security settings.";
        if(path.equals("auth/v1/verify"))return "The email confirmation code is incorrect or expired. Request a new confirmation email.";
        if(path.equals("auth/v1/signup")||path.equals("auth/v1/resend"))return "Cloud verification could not be completed. Check the email and password requirements, or try again later. Your offline vault is unchanged.";
        return "Cloud request was rejected. Check your sign-in and confirm your email.";
    }
}
