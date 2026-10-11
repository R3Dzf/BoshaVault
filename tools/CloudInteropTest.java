package com.bosha.vault;
import java.nio.file.*;
import java.util.*;
import org.json.*;

public final class CloudInteropTest {
    private static int passed;
    private static void check(boolean good,String label)throws Exception{if(!good)throw new Exception("FAIL "+label);System.out.println("PASS "+label);passed++;}
    interface Work{void run()throws Exception;}
    private static void reject(Work run,String label)throws Exception{boolean refused=false;try{run.run();}catch(Exception e){refused=true;}check(refused,label);}
    public static void main(String[] args)throws Exception{
        Path folder=Paths.get(args[0]);JSONObject profile=new JSONObject(Files.readString(folder.resolve("cloud-profile.json")));String slot=profile.getString("vaultSlot"),device=profile.getString("deviceSlot");
        byte[] key=CloudSnapshot.parseKey(profile.getString("recoveryKey")),inner=Files.readAllBytes(folder.resolve("dotnet.boshavault")),outer=Files.readAllBytes(folder.resolve("dotnet.cloud"));
        try{
            check(Arrays.equals(inner,CloudSnapshot.open(outer,key,slot,device)),".NET outer cloud envelope authenticates and opens in Java");
            JSONObject pair=CloudSnapshot.pairing(Files.readString(folder.resolve("cloud-pairing.txt")),profile.getString("vaultId"),profile.getString("ownerId"));
            check(pair.getString("recoveryKey").equals(profile.getString("recoveryKey")),".NET cloud connection code validates in Java");
            byte[] sealed=CloudSnapshot.seal(inner,key,slot,device);Files.write(folder.resolve("java.cloud"),sealed);
            check(Arrays.equals(inner,CloudSnapshot.open(sealed,key,slot,device)),"Java outer encryption roundtrip");
            byte[] corrupted=outer.clone();corrupted[corrupted.length-1]^=1;reject(()->CloudSnapshot.open(corrupted,key,slot,device),"Java rejects corrupted cloud ciphertext");
            reject(()->CloudSnapshot.open(outer,key,slot,UUID.randomUUID().toString()),"Java cloud authentication binds device slot");
            reject(()->CloudSnapshot.pairing(Files.readString(folder.resolve("cloud-pairing.txt")),UUID.randomUUID().toString(),profile.getString("ownerId")),"Java cloud pairing rejects unrelated local vault");
            Path a=folder.resolve("cloud-a.boshavault"),b=folder.resolve("cloud-b.boshavault");Files.copy(folder.resolve("dotnet.boshavault"),a,StandardCopyOption.REPLACE_EXISTING);Files.copy(a,b,StandardCopyOption.REPLACE_EXISTING);
            try(VaultEngine va=new VaultEngine(a.toFile(),UUID.randomUUID().toString());VaultEngine vb=new VaultEngine(b.toFile(),UUID.randomUUID().toString())){
                va.open("correct horse battery staple café 🔑");vb.open("correct horse battery staple café 🔑");vb.upsert(VaultEngine.blank().put("title","Synthetic cloud edit").put("password","OnlySyntheticSecret-345!"));
                byte[] before=va.export();reject(()->va.mergeCloud(Arrays.asList(vb.export(),new byte[200])),"Java cloud merge rejects invalid later snapshot");
                check(Arrays.equals(before,va.export()),"Java invalid cloud batch preserves local file exactly");
                va.mergeCloud(Collections.singletonList(vb.export()));check(va.snapshot().getJSONArray("entries").length()==2,"Java validated cloud batch applies remote edit");
            }
            System.out.println("RESULT: "+passed+" Java cloud crypto/pairing/atomic merge checks passed.");
        }finally{Arrays.fill(key,(byte)0);}
    }
}
