package com.bosha.vault;

import java.nio.file.*;
import java.util.*;
import org.json.*;

public final class VaultUxInteropTest {
    private static int passed;
    private static final String MASTER="synthetic UX master passphrase 2026";
    private static void check(boolean value,String name)throws Exception{if(!value)throw new Exception("FAIL "+name);System.out.println("PASS "+name);passed++;}
    private interface Work{void run()throws Exception;}
    private static void reject(Work work,String name)throws Exception{boolean refused=false;try{work.run();}catch(Exception e){refused=true;}check(refused,name);}
    private static JSONObject entry(JSONObject data,String id)throws Exception{JSONArray entries=data.getJSONArray("entries");for(int i=0;i<entries.length();i++)if(entries.getJSONObject(i).getString("id").equals(id))return entries.getJSONObject(i);throw new Exception("Synthetic fixture entry missing.");}
    public static void main(String[] args)throws Exception{
        Path folder=Paths.get(args[0]);JSONObject state=new JSONObject(Files.readString(folder.resolve("ux-state.json")));String reviewed=state.getString("reviewedId"),purged=state.getString("purgedId");
        check(new PasswordOptions().length==24,"Java password generator starts at 24");
        PasswordOptions rules=new PasswordOptions();rules.length=18;rules.lowercase=false;rules.allowedSymbols="@#";rules.minimumDigits=5;rules.minimumSymbols=3;boolean valid=true;
        for(int i=0;i<500;i++){String password=rules.generate();long digits=password.chars().filter(c->c>='0'&&c<='9').count(),symbols=password.chars().filter(c->c=='@'||c=='#').count();if(password.length()!=18||!password.matches("[ABCDEFGHJKLMNPQRSTUVWXYZ23456789@#]+")||digits<5||symbols<3)valid=false;}
        check(valid,"Java generator enforces selected alphabet and website minimums");
        PasswordOptions numeric=new PasswordOptions();numeric.length=8;numeric.lowercase=false;numeric.uppercase=false;numeric.symbols=false;check(numeric.generate().matches("[23456789]{8}"),"Java numeric-only website generation");
        PasswordOptions empty=new PasswordOptions();empty.lowercase=empty.uppercase=empty.digits=empty.symbols=false;reject(empty::generate,"Java rejects no character groups");
        PasswordOptions impossible=new PasswordOptions();impossible.length=8;impossible.minimumDigits=7;impossible.minimumSymbols=2;reject(impossible::generate,"Java rejects impossible minimum counts");
        PasswordOptions badSymbols=new PasswordOptions();badSymbols.allowedSymbols=" @";reject(badSymbols::generate,"Java rejects whitespace in symbols");
        Path output=folder.resolve("ux-java.boshavault");Files.copy(folder.resolve("ux-vault.boshavault"),output,StandardCopyOption.REPLACE_EXISTING);
        try(VaultEngine vault=new VaultEngine(output.toFile(),"22222222-2222-4222-8222-222222222222")){
            vault.open(MASTER);JSONObject body=vault.snapshot();check(body.getLong("schema")==2&&body.getJSONArray("folders").toString().contains("مشاريع"),".NET schema 2 and Unicode folder catalog open in Java");
            boolean accepted=false;for(PasswordHealth.Finding f:PasswordHealth.audit(body.getJSONArray("entries")))if(f.id.equals(reviewed))accepted=f.acknowledged;check(accepted,".NET review fingerprint is accepted by Java");
            check(entry(body,purged).getBoolean("purged")&&entry(body,purged).getString("password").isEmpty(),".NET permanent deletion hides all credentials in Java");
            check(vault.merge(Files.readAllBytes(folder.resolve("ux-stale.boshavault")))==0&&entry(vault.snapshot(),purged).getBoolean("purged"),"Java stale snapshot merge cannot resurrect a permanently deleted login");
            Path offline=folder.resolve("ux-java-offline.boshavault");Files.copy(folder.resolve("ux-stale.boshavault"),offline,StandardCopyOption.REPLACE_EXISTING);
            try(VaultEngine other=new VaultEngine(offline.toFile(),"33333333-3333-4333-8333-333333333333")){
                other.open(MASTER);JSONObject stale=entry(other.snapshot(),purged);stale.put("password","Synthetic concurrent Java edit 42!");other.upsert(stale);
                int before=vault.snapshot().getJSONArray("entries").length();check(vault.merge(other.export())==0&&vault.snapshot().getJSONArray("entries").length()==before,"Java concurrent edit cannot create a conflict copy of purged credentials");
                reject(()->vault.upsert(stale),"Java cannot save an old permanently deleted identity");other.merge(vault.export());
                check(entry(other.snapshot(),purged).getBoolean("purged")&&!Files.exists(Paths.get(offline+".previous")),"Java remote purge removes its previous automatic snapshot");
            }
            vault.addFolder("Java custom folder");JSONObject weak=VaultEngine.blank().put("title","Synthetic Java review").put("password","tiny").put("folder","Java custom folder");vault.upsert(weak);vault.acknowledgeReview(weak.getString("id"));
            check(PasswordHealth.audit(vault.snapshot().getJSONArray("entries")).stream().allMatch(f->f.acknowledged),"Java review confirmation removes current warnings and persists for reverse import");
            check(FolderCatalog.choices(vault.snapshot()).contains("Java custom folder")&&FolderCatalog.choices(vault.snapshot()).contains("Empty custom folder"),"Java persistent empty and custom folders remain available");
            reject(()->vault.addFolder(" \n"),"Java rejects blank folder names");
        }
        Path legacy=folder.resolve("ux-java-legacy.boshavault");Files.copy(folder.resolve("ux-legacy.boshavault"),legacy,StandardCopyOption.REPLACE_EXISTING);
        try(VaultEngine vault=new VaultEngine(legacy.toFile(),UUID.randomUUID().toString())){vault.open(MASTER);check(vault.snapshot().getLong("schema")==2&&FolderCatalog.choices(vault.snapshot()).contains("مشاريع"),"Java reads and upgrades actual schema 1 encrypted fixtures");vault.addFolder("After legacy upgrade");}
        try(VaultEngine vault=new VaultEngine(legacy.toFile(),UUID.randomUUID().toString())){vault.open(MASTER);check(FolderCatalog.choices(vault.snapshot()).contains("After legacy upgrade"),"Java upgraded vault saves and reopens");}
        System.out.println("RESULT: "+passed+" Java UX compatibility / deletion / review / generator checks passed.");
    }
}
