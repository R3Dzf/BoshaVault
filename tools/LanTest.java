package com.bosha.vault;
import java.io.*;
import java.nio.file.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import org.json.*;

public class LanTest {
    interface Work {void go()throws Exception;}
    static void reject(Work w,String name)throws Exception{boolean denied=false;try{w.go();}catch(Exception e){denied=true;}if(!denied)throw new Exception("FAIL "+name);System.out.println("PASS "+name);}
    static String encode(JSONObject o){return "BV1:"+Base64.getUrlEncoder().withoutPadding().encodeToString(o.toString().getBytes(StandardCharsets.UTF_8));}
    public static void main(String[] args)throws Exception{
        File dir=new File(args[0]);String code=Files.readString(new File(dir,"pairing.txt").toPath()).trim();JSONObject p=StrictJson.object(VaultEngine.utf8(Base64.getUrlDecoder().decode(code.substring(4))));
        JSONObject wrong=new JSONObject(p.toString());wrong.put("cert","0".repeat(64));reject(()->new LocalTransfer(encode(wrong)).download(),"LAN rejects changed TLS certificate pin");
        JSONObject badToken=new JSONObject(p.toString());badToken.put("token","A".repeat(43));reject(()->new LocalTransfer(encode(badToken)).download(),"LAN rejects wrong bearer token");
        LocalTransfer transfer=new LocalTransfer(code);byte[] bytes=transfer.download();File local=new File(dir,"lan-client.boshavault");Files.deleteIfExists(local.toPath());String password="correct horse battery staple café 🔑",device="22222222-2222-2222-2222-222222222222";VaultEngine.importNew(local,bytes,password,device);
        try(VaultEngine engine=new VaultEngine(local,device)){engine.open(password);engine.upsert(VaultEngine.blank().put("title","LAN test").put("username","lan@example.com").put("password","SyntheticLocalTransfer42!").put("url","https://example.com"));transfer.upload(engine.export());}
        System.out.println("PASS real Java ↔ .NET pinned TLS encrypted transfer");
        reject(transfer::download,"completed one-use pairing cannot be replayed");
        System.out.println("RESULT: 4 LAN client checks passed.");
    }
}
