package com.bosha.vault;

import java.nio.charset.StandardCharsets;
import java.util.*;
import org.json.*;

final class PasswordHealth {
    private static final Set<String> COMMON=new HashSet<>(Arrays.asList("password","password123","password1234","12345678","123456789","1234567890","qwerty123","qwerty1234","admin123","letmein","welcome123","iloveyou","adminadmin","passw0rd","password1!"));
    static final class Finding {
        final String id,title,fingerprint;final List<String> reasons;final boolean acknowledged;
        Finding(JSONObject e,List<String> r,String fingerprint)throws Exception{this.id=e.getString("id");this.title=e.getString("title");this.reasons=r;this.fingerprint=fingerprint;this.acknowledged=fingerprint.equals(e.optString("reviewAcknowledgement",""));}
    }
    private static boolean predictable(String password,String username){
        if(password.isEmpty())return true;String norm=password.toLowerCase(Locale.ROOT);if(COMMON.contains(norm))return true;
        if(password.length()>=8){boolean repeat=true;for(int i=1;i<password.length();i++)if(password.charAt(i)!=password.charAt(0))repeat=false;if(repeat)return true;}
        for(String part:new String[]{"qwerty","123456","abcdef","password"})if(norm.contains(part))return true;
        String user=username.split("@",-1)[0].trim().toLowerCase(Locale.ROOT);return user.length()>=5&&norm.contains(user);
    }
    static List<Finding> audit(JSONArray source)throws Exception{
        List<JSONObject> entries=new ArrayList<>();Map<String,List<String>> reuse=new HashMap<>();
        for(int i=0;i<source.length();i++){JSONObject e=source.getJSONObject(i);if(e.getBoolean("deleted"))continue;entries.add(e);String pw=e.getString("password");if(!pw.isEmpty())reuse.computeIfAbsent(pw,k->new ArrayList<>()).add(e.getString("id"));}
        Map<String,String> signatures=new HashMap<>();for(Map.Entry<String,List<String>> group:reuse.entrySet())if(group.getValue().size()>1){Collections.sort(group.getValue());signatures.put(group.getKey(),VaultEngine.hex(VaultEngine.sha(String.join(",",group.getValue()).getBytes(StandardCharsets.UTF_8))));}
        List<Finding> out=new ArrayList<>();
        for(JSONObject e:entries){String pw=e.getString("password"),username=e.getString("username");List<String> reasons=new ArrayList<>();List<String> peerIds=reuse.getOrDefault(pw,Collections.emptyList());
            if(pw.length()<16)reasons.add("Short or missing password");if(peerIds.size()>1)reasons.add("Password reused in another saved login");
            if(COMMON.contains(pw.toLowerCase(Locale.ROOT)))reasons.add("Common password pattern");else if(predictable(pw,username))reasons.add("Predictable password pattern");
            if(reasons.isEmpty())continue;String peers=signatures.getOrDefault(pw,"");
            Base64.Encoder b64=Base64.getEncoder();String material="BVREVIEW1|"+e.getString("id")+"|"+b64.encodeToString(pw.getBytes(StandardCharsets.UTF_8))+"|"+b64.encodeToString(username.getBytes(StandardCharsets.UTF_8))+"|"+String.join(";",reasons)+"|"+peers;
            out.add(new Finding(e,reasons,VaultEngine.hex(VaultEngine.sha(material.getBytes(StandardCharsets.UTF_8)))));
        }return out;
    }
}
