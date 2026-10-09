package com.bosha.vault;

import java.io.*;
import java.net.*;
import java.nio.*;
import java.nio.charset.*;
import java.nio.file.*;
import java.security.*;
import java.text.Normalizer;
import java.time.Instant;
import java.util.*;
import javax.crypto.*;
import javax.crypto.spec.*;
import org.json.*;
import org.bouncycastle.crypto.generators.Argon2BytesGenerator;
import org.bouncycastle.crypto.params.Argon2Parameters;

/** Same encrypted wire format as BoshaVault.Core. No Android dependencies. */
public final class VaultEngine implements AutoCloseable {
    public static final int MAX_BYTES=16*1024*1024;
    private static final SecureRandom RNG=new SecureRandom();
    private final File path; private final String device;
    private byte[] key, hash;
    private JSONObject envelope, data;
    public VaultEngine(File path,String device){this.path=path;this.device=device;}
    public synchronized boolean isOpen(){return key!=null;}
    public static byte[] random(int n){byte[] b=new byte[n];RNG.nextBytes(b);return b;}
    public static String b64(byte[] b){return Base64.getEncoder().encodeToString(b);}
    public static byte[] decode(String s,int n)throws Exception{
        if(s.length()>MAX_BYTES)throw new Exception("Invalid encoded field.");
        byte[] b=Base64.getDecoder().decode(s);
        if((n>=0&&b.length!=n)||!b64(b).equals(s))throw new Exception("Invalid encoded field.");return b;
    }
    public static String utf8(byte[] b)throws Exception{return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(b)).toString();}
    public static boolean id(String s){try{return UUID.fromString(s).toString().equals(s);}catch(Exception e){return false;}}
    public static byte[] derive(String password,JSONObject h)throws Exception{
        byte[] p=Normalizer.normalize(password,Normalizer.Form.NFC).getBytes(StandardCharsets.UTF_8);
        if(p.length>1024)throw new Exception("Passphrase is too long.");
        byte[] salt=decode(h.getString("salt"),16);
        Argon2Parameters params=new Argon2Parameters.Builder(Argon2Parameters.ARGON2_id).withVersion(Argon2Parameters.ARGON2_VERSION_13).withSalt(salt).withMemoryAsKB(65536).withIterations(3).withParallelism(4).build();
        try{Argon2BytesGenerator g=new Argon2BytesGenerator();g.init(params);byte[] out=new byte[32];g.generateBytes(p,out);return out;}
        finally{Arrays.fill(p,(byte)0);Arrays.fill(salt,(byte)0);params.clear();}
    }
    private static String prefix(JSONObject e)throws Exception{return "BoshaVault|1|"+e.getString("vaultId")+"|"+e.getLong("keyEpoch")+"|argon2id|65536|3|4|"+e.getJSONObject("kdf").getString("salt");}
    private static String wrapAad(JSONObject e)throws Exception{return prefix(e)+"|wrap";}
    private static String bodyAad(JSONObject e)throws Exception{JSONObject w=e.getJSONObject("wrap");return prefix(e)+"|payload|"+w.getString("nonce")+"|"+w.getString("ciphertext")+"|"+w.getString("tag");}
    private static JSONObject encrypt(byte[] k,byte[] plain,String aad)throws Exception{
        byte[] nonce=random(12);Cipher c=Cipher.getInstance("AES/GCM/NoPadding");c.init(Cipher.ENCRYPT_MODE,new SecretKeySpec(k,"AES"),new GCMParameterSpec(128,nonce));c.updateAAD(aad.getBytes(StandardCharsets.UTF_8));byte[] ct=c.doFinal(plain);
        return new JSONObject().put("nonce",b64(nonce)).put("ciphertext",b64(Arrays.copyOf(ct,ct.length-16))).put("tag",b64(Arrays.copyOfRange(ct,ct.length-16,ct.length)));
    }
    private static byte[] decrypt(byte[] k,JSONObject box,String aad)throws Exception{
        byte[] nonce=decode(box.getString("nonce"),12),ct=decode(box.getString("ciphertext"),-1),tag=decode(box.getString("tag"),16),combined=Arrays.copyOf(ct,ct.length+16);System.arraycopy(tag,0,combined,ct.length,16);
        Cipher c=Cipher.getInstance("AES/GCM/NoPadding");c.init(Cipher.DECRYPT_MODE,new SecretKeySpec(k,"AES"),new GCMParameterSpec(128,nonce));c.updateAAD(aad.getBytes(StandardCharsets.UTF_8));
        try{return c.doFinal(combined);}catch(AEADBadTagException e){throw new Exception("Incorrect passphrase or damaged / modified vault.");}
    }
    private static JSONObject parse(byte[] bytes)throws Exception{
        if(bytes.length<100||bytes.length>MAX_BYTES)throw new Exception("Invalid vault file size.");
        JSONObject e=StrictJson.object(utf8(bytes));StrictJson.keys(e,"format","version","vaultId","keyEpoch","kdf","wrap","payload");
        if(!e.getString("format").equals("BoshaVault")||StrictJson.number(e,"version")!=1||!id(e.getString("vaultId")))throw new Exception("Unsupported vault format.");
        if(StrictJson.number(e,"keyEpoch")<1||StrictJson.number(e,"keyEpoch")>1000000)throw new Exception("Invalid key epoch.");
        JSONObject h=e.getJSONObject("kdf");StrictJson.keys(h,"name","memoryKiB","iterations","parallelism","salt");
        if(!h.getString("name").equals("argon2id")||StrictJson.number(h,"memoryKiB")!=65536||StrictJson.number(h,"iterations")!=3||StrictJson.number(h,"parallelism")!=4)throw new Exception("Unsafe / unsupported key derivation parameters.");
        decode(h.getString("salt"),16);
        for(String n:new String[]{"wrap","payload"}){JSONObject b=e.getJSONObject(n);StrictJson.keys(b,"nonce","ciphertext","tag");decode(b.getString("nonce"),12);decode(b.getString("ciphertext"),n.equals("wrap")?32:-1);decode(b.getString("tag"),16);}
        return e;
    }
    public static String host(String url)throws Exception{
        URI u=new URI(url);String h=u.getHost();
        // java.net.URI does not expose Unicode DNS hosts. Punycode is accepted.
        if(!"https".equals(u.getScheme())||h==null||u.getRawUserInfo()!=null||(u.getPort()!=-1&&u.getPort()!=443))throw new Exception("Use an HTTPS website with no custom port or embedded credentials.");
        h=IDN.toASCII(h).toLowerCase(Locale.ROOT);if(h.endsWith("."))h=h.substring(0,h.length()-1);
        if(!h.contains(".")||h.length()>253||h.matches("[0-9.]+")||h.contains(":"))throw new Exception("Invalid website hostname.");return h;
    }
    private static JSONObject openBody(JSONObject e,byte[] k)throws Exception{
        byte[] plain=decrypt(k,e.getJSONObject("payload"),bodyAad(e));
        try{JSONObject d=StrictJson.object(utf8(plain));validateData(d,e.getString("vaultId"));return d;}finally{Arrays.fill(plain,(byte)0);}
    }
    private static void validateData(JSONObject d,String vaultId)throws Exception{
        StrictJson.keys(d,"schema","vaultId","revision","entries","trustedApps");
        if(StrictJson.number(d,"schema")!=1||!d.getString("vaultId").equals(vaultId)||StrictJson.number(d,"revision")<0)throw new Exception("Invalid vault contents.");
        JSONArray entries=d.getJSONArray("entries"),apps=d.getJSONArray("trustedApps");if(entries.length()>10000||apps.length()>200)throw new Exception("Vault limit exceeded.");
        Set<String> ids=new HashSet<>();
        for(int i=0;i<entries.length();i++){
            JSONObject x=entries.getJSONObject(i);StrictJson.keys(x,"id","title","username","password","url","notes","folder","favorite","deleted","updatedUtc","clock");
            if(!id(x.getString("id"))||!ids.add(x.getString("id")))throw new Exception("Invalid entry identity.");
            String[] fields={"title","username","password","url","notes","folder"};int[] sizes={200,2000,4096,2048,32000,100};
            for(int j=0;j<fields.length;j++)if(x.getString(fields[j]).length()>sizes[j])throw new Exception("Entry field too large.");
            if(x.getString("title").length()==0)throw new Exception("Entry needs a name.");
            if(!(x.get("favorite") instanceof Boolean)||!(x.get("deleted") instanceof Boolean))throw new Exception("Invalid boolean field.");
            Instant.parse(x.getString("updatedUtc"));if(x.getString("url").length()>0)host(x.getString("url"));
            JSONObject clocks=x.getJSONObject("clock");if(clocks.length()<1||clocks.length()>64)throw new Exception("Invalid revision clock.");
            for(Iterator<String> it=clocks.keys();it.hasNext();){String n=it.next();long v=StrictJson.number(clocks,n);if(!id(n)||v<1||v>1000000000L)throw new Exception("Invalid revision clock.");}
        }
        for(int i=0;i<apps.length();i++){JSONObject a=apps.getJSONObject(i);StrictJson.keys(a,"package","certificate","browser","entryIds");if(a.getString("package").length()<3||a.getString("package").length()>200||!a.getString("certificate").matches("[a-f0-9]{64}")||!(a.get("browser")instanceof Boolean))throw new Exception("Invalid trusted app.");JSONArray idsForApp=a.getJSONArray("entryIds");if(idsForApp.length()>10000)throw new Exception("Invalid app bindings.");for(int j=0;j<idsForApp.length();j++)if(!id(idsForApp.getString(j)))throw new Exception("Invalid app binding.");}
    }
    public synchronized void create(String password)throws Exception{
        newPassword(password);if(path.exists())throw new Exception("A vault already exists.");
        String vaultId=UUID.randomUUID().toString();JSONObject h=new JSONObject().put("name","argon2id").put("memoryKiB",65536).put("iterations",3).put("parallelism",4).put("salt",b64(random(16)));
        JSONObject env=new JSONObject().put("format","BoshaVault").put("version",1).put("vaultId",vaultId).put("keyEpoch",1).put("kdf",h);byte[] secret=random(32),kek=derive(password,h);
        try{env.put("wrap",encrypt(kek,secret,wrapAad(env)));JSONObject body=new JSONObject().put("schema",1).put("vaultId",vaultId).put("revision",0).put("entries",new JSONArray()).put("trustedApps",new JSONArray());byte[] out=seal(env,body,secret);write(out,null,false);key=secret;envelope=env;data=body;hash=sha(out);}
        catch(Exception e){Arrays.fill(secret,(byte)0);throw e;}finally{Arrays.fill(kek,(byte)0);}
    }
    public synchronized void open(String password)throws Exception{
        byte[] bytes=read(path),secret=null;JSONObject e=parse(bytes);byte[] kek=derive(password,e.getJSONObject("kdf"));
        try{secret=decrypt(kek,e.getJSONObject("wrap"),wrapAad(e));JSONObject body=openBody(e,secret);close();key=secret;envelope=e;data=body;hash=sha(bytes);}
        catch(Exception ex){if(secret!=null)Arrays.fill(secret,(byte)0);throw ex;}finally{Arrays.fill(kek,(byte)0);}
    }
    public synchronized void openWithKey(byte[] secret)throws Exception{byte[] bytes=read(path);JSONObject e=parse(bytes),body=openBody(e,secret);close();key=secret.clone();envelope=e;data=body;hash=sha(bytes);}
    public synchronized byte[] biometricKey()throws Exception{ensure();return key.clone();}
    public synchronized JSONObject snapshot()throws Exception{ensure();return new JSONObject(data.toString());}
    public static JSONObject blank()throws Exception{return new JSONObject().put("id",UUID.randomUUID().toString()).put("title","").put("username","").put("password","").put("url","").put("notes","").put("folder","Personal").put("favorite",false).put("deleted",false).put("updatedUtc",Instant.now().toString()).put("clock",new JSONObject());}
    public synchronized void upsert(JSONObject entry)throws Exception{
        ensure();JSONObject candidate=new JSONObject(data.toString()),copy=new JSONObject(entry.toString());JSONArray list=candidate.getJSONArray("entries"),next=new JSONArray();JSONObject clocks=new JSONObject();
        for(int i=0;i<list.length();i++){JSONObject x=list.getJSONObject(i);if(x.getString("id").equals(copy.getString("id")))clocks=new JSONObject(x.getJSONObject("clock").toString());else next.put(x);}
        tick(clocks,device);copy.put("clock",clocks).put("updatedUtc",Instant.now().toString());next.put(copy);candidate.put("entries",next).put("revision",candidate.getLong("revision")+1);commit(candidate);
    }
    public synchronized int merge(byte[] bytes)throws Exception{
        ensure();JSONObject remoteEnv=parse(bytes);if(!remoteEnv.getString("vaultId").equals(envelope.getString("vaultId")))throw new Exception("This file belongs to a different vault.");
        if(remoteEnv.getLong("keyEpoch")!=envelope.getLong("keyEpoch"))throw new Exception("This backup uses a rotated key. Merge with its master passphrase.");
        JSONObject remote=openBody(remoteEnv,key),candidate=new JSONObject(data.toString());int conflicts=mergeData(candidate,remote,device);commit(candidate);return conflicts;
    }
    public synchronized boolean needsPassphrase(byte[] bytes)throws Exception{ensure();return parse(bytes).getLong("keyEpoch")!=envelope.getLong("keyEpoch");}
    public synchronized int mergeUsingPassword(byte[] bytes,String incomingPassword)throws Exception{
        ensure();JSONObject remoteEnv=parse(bytes);if(!remoteEnv.getString("vaultId").equals(envelope.getString("vaultId")))throw new Exception("Different vault identity.");byte[] kek=derive(incomingPassword,remoteEnv.getJSONObject("kdf")),remoteKey=null;
        try{remoteKey=decrypt(kek,remoteEnv.getJSONObject("wrap"),wrapAad(remoteEnv));JSONObject remote=openBody(remoteEnv,remoteKey),candidate=new JSONObject(data.toString());if(remoteEnv.getLong("keyEpoch")==envelope.getLong("keyEpoch")&&!MessageDigest.isEqual(key,remoteKey))throw new Exception("Both devices rotated independently. Recover their backups separately.");int conflicts=mergeData(candidate,remote,device);
            if(remoteEnv.getLong("keyEpoch")>envelope.getLong("keyEpoch")){byte[] merged=seal(remoteEnv,candidate,remoteKey);write(merged,hash,false);Files.deleteIfExists(new File(path+".previous").toPath());Arrays.fill(key,(byte)0);key=remoteKey;remoteKey=null;envelope=remoteEnv;data=candidate;hash=sha(merged);}else commit(candidate);return conflicts;
        }finally{Arrays.fill(kek,(byte)0);if(remoteKey!=null)Arrays.fill(remoteKey,(byte)0);}
    }
    public synchronized void trust(String pkg,String cert,boolean browser,String entryId)throws Exception{
        ensure();JSONObject candidate=new JSONObject(data.toString());JSONArray a=candidate.getJSONArray("trustedApps"),next=new JSONArray();
        JSONArray entries=new JSONArray();for(int i=0;i<a.length();i++){JSONObject existing=a.getJSONObject(i);if(!existing.getString("package").equals(pkg))next.put(existing);else if(existing.getString("certificate").equals(cert)&&existing.getBoolean("browser")==browser)entries=new JSONArray(existing.getJSONArray("entryIds").toString());}
        if(entryId!=null){boolean found=false;for(int i=0;i<entries.length();i++)found|=entries.getString(i).equals(entryId);if(!found)entries.put(entryId);}
        next.put(new JSONObject().put("package",pkg).put("certificate",cert).put("browser",browser).put("entryIds",entries));candidate.put("trustedApps",next).put("revision",candidate.getLong("revision")+1);commit(candidate);
    }
    public synchronized void revokeTrust()throws Exception{ensure();JSONObject candidate=new JSONObject(data.toString());candidate.put("trustedApps",new JSONArray()).put("revision",candidate.getLong("revision")+1);commit(candidate);}
    public synchronized boolean trusted(String pkg,String cert,boolean browser)throws Exception{ensure();JSONArray a=data.getJSONArray("trustedApps");for(int i=0;i<a.length();i++){JSONObject x=a.getJSONObject(i);if(x.getString("package").equals(pkg)&&x.getString("certificate").equals(cert)&&x.getBoolean("browser")==browser)return true;}return false;}
    public synchronized boolean linked(String pkg,String cert,String entryId)throws Exception{ensure();JSONArray a=data.getJSONArray("trustedApps");for(int i=0;i<a.length();i++){JSONObject x=a.getJSONObject(i);if(x.getString("package").equals(pkg)&&x.getString("certificate").equals(cert)&&!x.getBoolean("browser")){JSONArray ids=x.getJSONArray("entryIds");for(int j=0;j<ids.length();j++)if(ids.getString(j).equals(entryId))return true;}}return false;}
    public synchronized byte[] export()throws Exception{ensure();byte[] bytes=read(path);if(!MessageDigest.isEqual(hash,sha(bytes)))throw new Exception("The vault changed outside this session. Reopen it.");return bytes;}
    public synchronized void changePassword(String current,String next)throws Exception{
        ensure();newPassword(next);byte[] kek=derive(current,envelope.getJSONObject("kdf")),check=null;
        try{check=decrypt(kek,envelope.getJSONObject("wrap"),wrapAad(envelope));if(!MessageDigest.isEqual(check,key))throw new Exception("Incorrect current passphrase.");}finally{Arrays.fill(kek,(byte)0);if(check!=null)Arrays.fill(check,(byte)0);}
        JSONObject env=new JSONObject(envelope.toString()),body=new JSONObject(data.toString());env.put("keyEpoch",env.getLong("keyEpoch")+1);env.getJSONObject("kdf").put("salt",b64(random(16)));kek=derive(next,env.getJSONObject("kdf"));byte[] nextKey=random(32);
        try{env.put("wrap",encrypt(kek,nextKey,wrapAad(env)));body.put("revision",body.getLong("revision")+1);byte[] bytes=seal(env,body,nextKey);write(bytes,hash,false);Files.deleteIfExists(new File(path+".previous").toPath());Arrays.fill(key,(byte)0);key=nextKey;nextKey=new byte[0];envelope=env;data=body;hash=sha(bytes);}finally{Arrays.fill(kek,(byte)0);Arrays.fill(nextKey,(byte)0);}
    }
    public static void importNew(File destination,byte[] bytes,String password,String device)throws Exception{
        File staged=new File(destination.getParentFile(),UUID.randomUUID()+".import");
        try{Files.write(staged.toPath(),bytes);try(VaultEngine test=new VaultEngine(staged,device)){test.open(password);}try(VaultEngine target=new VaultEngine(destination,device)){target.write(bytes,null,false);}}
        finally{Files.deleteIfExists(staged.toPath());Files.deleteIfExists(new File(staged+".lock").toPath());}
    }
    private static void newPassword(String p)throws Exception{if(p.length()<16||p.getBytes(StandardCharsets.UTF_8).length>1024)throw new Exception("Use a master passphrase with at least 16 characters.");}
    private synchronized void commit(JSONObject candidate)throws Exception{byte[] bytes=seal(envelope,candidate,key);write(bytes,hash,true);data=candidate;hash=sha(bytes);}
    private static byte[] seal(JSONObject env,JSONObject body,byte[] secret)throws Exception{
        validateData(body,env.getString("vaultId"));byte[] plain=body.toString().getBytes(StandardCharsets.UTF_8);
        try{env.put("payload",encrypt(secret,plain,bodyAad(env)));byte[] bytes=env.toString().getBytes(StandardCharsets.UTF_8);if(bytes.length>MAX_BYTES)throw new Exception("Vault is too large.");return bytes;}finally{Arrays.fill(plain,(byte)0);}
    }
    public static byte[] read(File path)throws Exception{try(InputStream in=new FileInputStream(path)){return bounded(in);}}
    public static byte[] bounded(InputStream in)throws Exception{ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] buf=new byte[8192];int n;while((n=in.read(buf))!=-1){if(out.size()+n>MAX_BYTES)throw new Exception("Vault file is too large.");out.write(buf,0,n);}return out.toByteArray();}
    private void write(byte[] bytes,byte[] expected,boolean previous)throws Exception{
        path.getParentFile().mkdirs();File lock=new File(path+".lock");
        try(RandomAccessFile raf=new RandomAccessFile(lock,"rw");java.nio.channels.FileLock l=raf.getChannel().lock()){
            if(expected==null&&path.exists())throw new Exception("Refusing to replace an existing vault.");
            if(expected!=null&&(!path.exists()||!MessageDigest.isEqual(expected,sha(read(path)))))throw new Exception("Another session changed this vault. Reopen it.");
            File tmp=new File(path.getParentFile(),UUID.randomUUID()+".tmp");
            try{try(FileOutputStream stream=new FileOutputStream(tmp)){stream.write(bytes);stream.getFD().sync();}tmp.setReadable(false,false);tmp.setWritable(false,false);tmp.setReadable(true,true);tmp.setWritable(true,true);
                if(previous&&path.exists())Files.copy(path.toPath(),new File(path+".previous").toPath(),StandardCopyOption.REPLACE_EXISTING);
                Files.move(tmp.toPath(),path.toPath(),StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);
            }finally{Files.deleteIfExists(tmp.toPath());}
        }
    }
    public static byte[] sha(byte[] b)throws Exception{return MessageDigest.getInstance("SHA-256").digest(b);}
    public static String hex(byte[] b){StringBuilder s=new StringBuilder();for(byte x:b)s.append(String.format(Locale.ROOT,"%02x",x&255));return s.toString();}
    private void ensure()throws Exception{if(key==null)throw new Exception("Vault is locked.");}
    public synchronized void close(){if(key!=null)Arrays.fill(key,(byte)0);key=null;data=null;envelope=null;hash=null;}
    public static String generate(int length,boolean symbols)throws Exception{
        if(length<16||length>128)throw new Exception("Use 16–128 characters.");String[] groups=symbols?new String[]{"abcdefghijkmnopqrstuvwxyz","ABCDEFGHJKLMNPQRSTUVWXYZ","23456789","!@#$%&*+-=?_"}:new String[]{"abcdefghijkmnopqrstuvwxyz","ABCDEFGHJKLMNPQRSTUVWXYZ","23456789"};String alphabet=String.join("",groups);char[] out=new char[length];
        for(int i=0;i<groups.length;i++)out[i]=groups[i].charAt(RNG.nextInt(groups[i].length()));for(int i=groups.length;i<length;i++)out[i]=alphabet.charAt(RNG.nextInt(alphabet.length()));for(int i=length-1;i>0;i--){int j=RNG.nextInt(i+1);char c=out[i];out[i]=out[j];out[j]=c;}return new String(out);
    }
    static int compare(JSONObject a,JSONObject b)throws Exception{boolean gt=false,lt=false;Set<String> keys=union(a,b);for(String k:keys){long x=a.optLong(k,0),y=b.optLong(k,0);gt|=x>y;lt|=x<y;}return gt&&lt?2:gt?1:lt?-1:0;}
    private static Set<String> union(JSONObject a,JSONObject b){Set<String>s=new TreeSet<>();a.keys().forEachRemaining(s::add);b.keys().forEachRemaining(s::add);return s;}
    private static void tick(JSONObject c,String device)throws Exception{c.put(device,c.optLong(device,0)+1);if(c.length()>64||c.getLong(device)>1000000000L)throw new Exception("Device revision limit reached.");}
    private static String vector(JSONObject c)throws Exception{List<String> fields=new ArrayList<>();for(String k:union(c,new JSONObject()))fields.add(k+":"+c.getLong(k));return String.join(";",fields);}
    private static boolean same(JSONObject a,JSONObject b)throws Exception{for(String k:new String[]{"title","username","password","url","notes","folder","favorite","deleted"})if(!a.get(k).equals(b.get(k)))return false;return true;}
    public static int mergeData(JSONObject local,JSONObject remote,String device)throws Exception{
        if(!local.getString("vaultId").equals(remote.getString("vaultId")))throw new Exception("Different vault identity.");LinkedHashMap<String,JSONObject> map=new LinkedHashMap<>();JSONArray a=local.getJSONArray("entries"),b=remote.getJSONArray("entries");for(int i=0;i<a.length();i++){JSONObject x=a.getJSONObject(i);map.put(x.getString("id"),new JSONObject(x.toString()));}int conflicts=0;
        for(int i=0;i<b.length();i++){
            JSONObject incoming=b.getJSONObject(i),current=map.get(incoming.getString("id"));if(current==null){map.put(incoming.getString("id"),new JSONObject(incoming.toString()));continue;}
            JSONObject ca=current.getJSONObject("clock"),cb=incoming.getJSONObject("clock");int r=compare(ca,cb);if(r==1)continue;if(r==-1){map.put(incoming.getString("id"),new JSONObject(incoming.toString()));continue;}boolean equal=same(current,incoming);if(r==0){if(!equal)throw new Exception("Conflicting content with an identical revision clock.");continue;}
            JSONObject joined=new JSONObject();for(String k:union(ca,cb))joined.put(k,Math.max(ca.optLong(k,0),cb.optLong(k,0)));tick(joined,device);
            if(equal){current.put("clock",joined);continue;}
            String[] vectors={vector(ca),vector(cb)};Arrays.sort(vectors);String h=hex(sha((current.getString("id")+"|"+String.join("|",vectors)).getBytes(StandardCharsets.UTF_8))).substring(0,32);String conflict=h.substring(0,8)+"-"+h.substring(8,12)+"-"+h.substring(12,16)+"-"+h.substring(16,20)+"-"+h.substring(20);
            boolean deletion=incoming.getBoolean("deleted")&&!current.getBoolean("deleted");JSONObject primary=new JSONObject((deletion?incoming:current).toString()),alternate=new JSONObject((deletion?current:incoming).toString());primary.put("clock",joined);map.put(current.getString("id"),primary);
            String title=alternate.getString("title");alternate.put("id",conflict).put("title",title.substring(0,Math.min(180,title.length()))+" (conflict)").put("clock",new JSONObject(joined.toString()));if(!map.containsKey(conflict)){map.put(conflict,alternate);conflicts++;}
        }
        if(map.size()>10000)throw new Exception("Merged vault exceeds the entry limit.");JSONArray result=new JSONArray();for(JSONObject x:map.values())result.put(x);local.put("entries",result).put("revision",Math.max(local.getLong("revision"),remote.getLong("revision"))+1);return conflicts;
    }
}
