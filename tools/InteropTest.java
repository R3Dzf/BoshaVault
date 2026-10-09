package com.bosha.vault;
import java.io.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import org.json.*;

public class InteropTest {
    static int passed;
    static void check(boolean value,String name)throws Exception{if(!value)throw new Exception("FAIL "+name);System.out.println("PASS "+name);passed++;}
    interface Action{void run()throws Exception;}
    static void reject(Action a,String name)throws Exception{boolean denied=false;try{a.run();}catch(Exception e){denied=true;}check(denied,name);}
    public static void main(String[] args)throws Exception{
        File folder=new File(args[0]);String password="correct horse battery staple café 🔑",devA="11111111-1111-1111-1111-111111111111",devB="22222222-2222-2222-2222-222222222222";
        File output=new File(folder,"java.boshavault"),copy=new File(folder,"java-copy.boshavault");Files.deleteIfExists(output.toPath());
        Files.copy(new File(folder,"dotnet.boshavault").toPath(),output.toPath());
        try(VaultEngine e=new VaultEngine(output,devB)){
            e.open(password);JSONObject data=e.snapshot();JSONObject item=data.getJSONArray("entries").getJSONObject(0);
            check(item.getString("password").equals("OnlySyntheticTestSecret-7!")&&item.getString("notes").equals("ملاحظات تجريبية"),".NET encrypted file opens in Java with Unicode");
            JSONObject added=VaultEngine.blank().put("title","Java demo").put("username","java@example.com").put("password","JavaSyntheticSecret-42!").put("url","https://example.com");e.upsert(added);
            check(e.snapshot().getJSONArray("entries").length()==2,"Java CRUD persists encrypted entry");
            String bytes=VaultEngine.utf8(e.export());check(!bytes.contains("JavaSyntheticSecret")&&!bytes.contains("Java demo"),"Java never stores plaintext metadata");
            reject(()->{try(VaultEngine wrong=new VaultEngine(output,devA)){wrong.open("incorrect passphrase");}},"Java wrong passphrase rejected");
            String nonce=new JSONObject(bytes).getJSONObject("payload").getString("nonce");e.upsert(added);check(!new JSONObject(VaultEngine.utf8(e.export())).getJSONObject("payload").getString("nonce").equals(nonce),"Java nonce fresh on save");
            Files.copy(output.toPath(),copy.toPath(),StandardCopyOption.REPLACE_EXISTING);
            try(VaultEngine other=new VaultEngine(copy,devA)){other.open(password);JSONObject change=e.snapshot().getJSONArray("entries").getJSONObject(0);JSONObject sibling=other.snapshot().getJSONArray("entries").getJSONObject(0);change.put("password","local-branch-password!");sibling.put("password","remote-branch-password!");e.upsert(change);other.upsert(sibling);check(e.merge(other.export())==1,"Java conflict keeps both edits");check(e.merge(other.export())==0,"Java repeat merge is idempotent");}
            byte[] before=e.export();reject(()->e.merge(new byte[20]),"Java bad import rejected");check(Arrays.equals(before,e.export()),"Java bad import does not damage file");
            byte[] oldKey=e.biometricKey();String next="a newer strong master passphrase 123";
            reject(()->e.changePassword("wrong",next),"Java password change requires current passphrase");
            e.changePassword(password,next);
            reject(()->{try(VaultEngine old=new VaultEngine(output,devB)){old.openWithKey(oldKey);}},"Java old decrypted key fails after rotation");Arrays.fill(oldKey,(byte)0);
            reject(()->{try(VaultEngine old=new VaultEngine(output,devB)){old.open(password);}},"Java old master fails after rotation");
            try(VaultEngine other=new VaultEngine(copy,devA)){other.open(password);other.mergeUsingPassword(e.export(),next);check(new JSONObject(VaultEngine.utf8(other.export())).getLong("keyEpoch")==2,"Java another device adopts rotated key");}
            e.mergeUsingPassword(before,password);check(new JSONObject(VaultEngine.utf8(e.export())).getLong("keyEpoch")==2,"Java old backup cannot downgrade vault key");
            e.close();reject(e::export,"Java locked vault refuses access");
        }
        // Recreate fixture without conflict editing for the independent .NET verifier.
        Files.deleteIfExists(output.toPath());VaultEngine.importNew(output,Files.readAllBytes(new File(folder,"dotnet.boshavault").toPath()),password,devB);
        try(VaultEngine e=new VaultEngine(output,devB)){e.open(password);e.upsert(VaultEngine.blank().put("title","Java demo").put("username","java@example.com").put("password","JavaSyntheticSecret-42!").put("url","https://example.com"));}
        JSONObject base=StrictJson.object(Files.readString(new File(folder,"merge-base.json").toPath()));JSONObject a=new JSONObject(base.toString()),b=new JSONObject(base.toString());JSONObject ca=a.getJSONArray("entries").getJSONObject(0),cb=b.getJSONArray("entries").getJSONObject(0);ca.put("password","Synthetic-A");ca.getJSONObject("clock").put(devA,2);cb.put("password","Synthetic-B");cb.getJSONObject("clock").put(devB,1);VaultEngine.mergeData(a,b,devA);
        JSONObject expected=new JSONObject(Files.readString(new File(folder,"merge-expected.json").toPath()));Set<String> actualIds=new HashSet<>(),expectedIds=new HashSet<>();for(int i=0;i<a.getJSONArray("entries").length();i++)actualIds.add(a.getJSONArray("entries").getJSONObject(i).getString("id"));for(int i=0;i<expected.getJSONArray("entries").length();i++)expectedIds.add(expected.getJSONArray("entries").getJSONObject(i).getString("id"));check(actualIds.equals(expectedIds),"deterministic conflict IDs match .NET");
        byte[] fixture=Files.readAllBytes(new File(folder,"dotnet.boshavault").toPath());String raw=VaultEngine.utf8(fixture);
        reject(()->StrictJson.object("{\"a\":1,\"a\":2}"),"Java duplicate properties rejected");
        reject(()->StrictJson.object("{\"a\":1,}"),"Java trailing comma rejected");
        reject(()->StrictJson.object("{\"a\":01}"),"Java malformed number rejected");
        reject(()->StrictJson.object("{\"a\":\"\\x\"}"),"Java malformed escape rejected");
        reject(()->StrictJson.object("{a:1}"),"Java unquoted properties rejected");
        File bad=new File(folder,"java-bad.boshavault");JSONObject env=new JSONObject(raw);env.getJSONObject("kdf").put("memoryKiB",Integer.MAX_VALUE);Files.writeString(bad.toPath(),env.toString());reject(()->{try(VaultEngine e=new VaultEngine(bad,devB)){e.open(password);}},"Java hostile KDF parameters rejected");
        env=new JSONObject(raw);byte[] ct=VaultEngine.decode(env.getJSONObject("payload").getString("ciphertext"),-1);ct[0]^=1;env.getJSONObject("payload").put("ciphertext",VaultEngine.b64(ct));Files.writeString(bad.toPath(),env.toString());reject(()->{try(VaultEngine e=new VaultEngine(bad,devB)){e.open(password);}},"Java tampered ciphertext rejected");
        check(VaultEngine.host("https://example.com/login").equals("example.com"),"Java exact HTTPS host normalization");reject(()->VaultEngine.host("http://example.com"),"Java insecure website rejected");reject(()->VaultEngine.host("https://evil.test@example.com"),"Java embedded username rejected");reject(()->VaultEngine.host("https://example.com:8443"),"Java custom port rejected");
        Set<String> values=new HashSet<>();for(int i=0;i<1000;i++){String p=VaultEngine.generate(24,true);checkNoCount(p.length()==24&&p.matches(".*[a-z].*")&&p.matches(".*[A-Z].*")&&p.matches(".*[0-9].*")&&p.matches(".*[!@#$%&*+\\-=?_].*"));values.add(p);}check(values.size()==1000,"Java random password sample has groups and no duplicates");
        reject(()->new LocalTransfer("BV1:"+Base64.getUrlEncoder().withoutPadding().encodeToString(new JSONObject().put("v",1).put("host","8.8.8.8").put("port",12345).put("cert","0".repeat(64)).put("token",Base64.getUrlEncoder().withoutPadding().encodeToString(new byte[32])).toString().getBytes(StandardCharsets.UTF_8))),"LAN pairing rejects public IP addresses");
        System.out.println("RESULT: "+passed+" Java checks passed.");
    }
    static void checkNoCount(boolean b)throws Exception{if(!b)throw new Exception("Generator invariant failed.");}
}
