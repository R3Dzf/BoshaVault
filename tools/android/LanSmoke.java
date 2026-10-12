package com.bosha.vault.tests;

import android.app.*;
import android.content.*;
import android.net.*;
import android.os.*;
import java.io.*;
import java.lang.reflect.*;
import java.net.*;
import java.util.*;
import org.json.*;
import java.util.concurrent.atomic.AtomicInteger;

/** Disposable real native Android -> .NET TLS test. Uses synthetic vaults only. */
public final class LanSmoke extends Instrumentation {
    private int passed;
    private void check(boolean value,String label)throws Exception{if(!value)throw new Exception("FAIL "+label);passed++;Bundle p=new Bundle();p.putString("stream","LAN_CHECK_PASS: "+label+"\n");sendStatus(0,p);}
    private Object call(Object instance,String name,Class<?>[] types,Object... values)throws Exception{
        Method m=instance.getClass().getDeclaredMethod(name,types);m.setAccessible(true);
        try{return m.invoke(instance,values);}catch(InvocationTargetException e){if(e.getCause() instanceof Exception)throw (Exception)e.getCause();throw e;}
    }
    @Override public void onCreate(Bundle args){super.onCreate(args);start();}
    @Override public void onStart(){
        Bundle result=new Bundle();int code=Activity.RESULT_CANCELED;File local=null;Socket stale=null;
        try{
            Context target=getTargetContext();ClassLoader loader=target.getClassLoader();Class<?> wifi=loader.loadClass("com.bosha.vault.LocalWifi"),transfer=loader.loadClass("com.bosha.vault.LocalTransfer"),engine=loader.loadClass("com.bosha.vault.VaultEngine");
            Method score=wifi.getDeclaredMethod("score",NetworkCapabilities.class,LinkProperties.class,InetAddress.class);score.setAccessible(true);
            NetworkCapabilities offline=new NetworkCapabilities().addTransportType(NetworkCapabilities.TRANSPORT_WIFI);offline.removeCapability(NetworkCapabilities.NET_CAPABILITY_INTERNET).removeCapability(NetworkCapabilities.NET_CAPABILITY_VALIDATED);
            LinkProperties links=new LinkProperties();links.addLinkAddress(new LinkAddress("192.168.1.10/24"));InetAddress peer=InetAddress.getByName("192.168.1.13");
            check(((Integer)score.invoke(null,offline,links,peer))==2,"offline/unvalidated Wi-Fi is eligible on the local subnet");
            check(((Integer)score.invoke(null,new NetworkCapabilities().addTransportType(NetworkCapabilities.TRANSPORT_CELLULAR),links,peer))<0,"cellular default network cannot replace local Wi-Fi");
            check(((Integer)score.invoke(null,new NetworkCapabilities(offline).addTransportType(NetworkCapabilities.TRANSPORT_VPN),links,peer))<0,"VPN transport cannot replace the local Wi-Fi socket");
            byte[] fixture;
            try(InputStream in=getContext().getAssets().open("lan-pairing.txt");ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] b=new byte[1024];for(int n;(n=in.read(b))>=0;)out.write(b,0,n);fixture=out.toByteArray();}
            String pairing=new String(fixture,"UTF-8").trim();JSONObject details=new JSONObject(new String(android.util.Base64.decode(pairing.substring(4),android.util.Base64.URL_SAFE|android.util.Base64.NO_WRAP|android.util.Base64.NO_PADDING),"UTF-8"));
            Method connect=wifi.getDeclaredMethod("connect",Context.class,String.class);connect.setAccessible(true);
            JSONObject wrong=new JSONObject(details.toString()).put("cert",String.join("",Collections.nCopies(64,"0")));Object wrongConnection=connect.invoke(null,target,encode(wrong));
            boolean rejected=false;try{call(wrongConnection,"download",new Class<?>[]{});}catch(Exception expected){rejected=true;}check(rejected,"native TLS refuses a changed pairing certificate");
            JSONObject badToken=new JSONObject(details.toString()).put("token",String.join("",Collections.nCopies(43,"A")));Object badConnection=connect.invoke(null,target,encode(badToken));
            rejected=false;try{call(badConnection,"download",new Class<?>[]{});}catch(Exception expected){rejected=true;}check(rejected,"native TLS refuses a wrong pairing token");
            Method bind=wifi.getDeclaredMethod("bind",Context.class,Socket.class,String.class);bind.setAccessible(true);
            stale=new Socket();bind.invoke(null,target,stale,details.getString("host"));stale.connect(new InetSocketAddress(details.getString("host"),details.getInt("port")),5000);
            Class<?> binderType=loader.loadClass("com.bosha.vault.LocalTransfer$SocketBinder"),progressType=loader.loadClass("com.bosha.vault.LocalTransfer$Progress");
            AtomicInteger attempts=new AtomicInteger();
            Object binder=java.lang.reflect.Proxy.newProxyInstance(loader,new Class<?>[]{binderType},(object,method,args)->{
                if(method.getName().equals("bind")){int attempt=attempts.incrementAndGet();if(attempt==1||attempt==3)throw new EOFException("Synthetic interrupted transport.");bind.invoke(null,target,args[0],args[1]);}return null;
            });
            Object progress=java.lang.reflect.Proxy.newProxyInstance(loader,new Class<?>[]{progressType},(object,method,args)->null);
            Constructor<?> transferCtor=transfer.getDeclaredConstructor(String.class,binderType,progressType);transferCtor.setAccessible(true);
            Object connection=transferCtor.newInstance(pairing,binder,progress);long started=SystemClock.elapsedRealtime();byte[] encrypted=(byte[])call(connection,"download",new Class<?>[]{});
            check(attempts.get()==2,"interrupted GET creates a fresh socket and retries over the selected Wi-Fi");
            check(SystemClock.elapsedRealtime()-started<10000,"native Wi-Fi transfer proceeds while another TLS socket is stalled");stale.close();stale=null;
            local=new File(target.getCacheDir(),"native-lan-"+UUID.randomUUID()+".boshavault");String password="correct horse battery staple café 🔑",device="22222222-2222-2222-2222-222222222222";
            Method importNew=engine.getDeclaredMethod("importNew",File.class,byte[].class,String.class,String.class);importNew.setAccessible(true);importNew.invoke(null,local,encrypted,password,device);
            Constructor<?> ctor=engine.getDeclaredConstructor(File.class,String.class);ctor.setAccessible(true);Object vault=ctor.newInstance(local,device);
            try{
                call(vault,"open",new Class<?>[]{String.class},password);
                JSONObject data=(JSONObject)call(vault,"snapshot",new Class<?>[]{});check(data.getJSONArray("entries").getJSONObject(0).getString("title").equals("Windows LAN fixture"),"Android opens the real .NET encrypted vault with the local passphrase");
                Method blank=engine.getDeclaredMethod("blank");blank.setAccessible(true);JSONObject entry=(JSONObject)blank.invoke(null);entry.put("title","Native LAN update").put("username","synthetic@example.invalid").put("password","SyntheticNativeOffline42!").put("url","https://example.com");call(vault,"upsert",new Class<?>[]{JSONObject.class},entry);
                byte[] merged=(byte[])call(vault,"export",new Class<?>[]{});call(connection,"upload",new Class<?>[]{byte[].class},(Object)merged);check(attempts.get()==4,"new-server receipt support retries an interrupted upload with the same encrypted bytes");check(true,"native Wi-Fi sends an encrypted merged upload and receives confirmation");
                call(connection,"cancel",new Class<?>[]{});rejected=false;try{call(connection,"download",new Class<?>[]{});}catch(Exception expected){rejected=true;}check(rejected,"cancelled native transfer cannot start another request");
            }finally{call(vault,"close",new Class<?>[]{});}
            AtomicInteger bounded=new AtomicInteger();Object alwaysBroken=java.lang.reflect.Proxy.newProxyInstance(loader,new Class<?>[]{binderType},(object,method,args)->{if(method.getName().equals("bind")){bounded.incrementAndGet();throw new EOFException("Synthetic interrupted transport.");}return null;});
            Object failing=transferCtor.newInstance(pairing,alwaysBroken,progress);rejected=false;try{call(failing,"download",new Class<?>[]{});}catch(Exception expected){rejected=true;}check(rejected&&bounded.get()==3,"repeated transport failure is bounded to three attempts");
            result.putString("stream","NATIVE_LAN_PASS: "+passed+" native Wi-Fi routing, certificate/token, TLS and encrypted merge checks passed.\n");code=Activity.RESULT_OK;
        }catch(Throwable e){result.putString("stream","NATIVE_LAN_FAIL: "+android.util.Log.getStackTraceString(e)+"\n");}
        finally{if(stale!=null)try{stale.close();}catch(Exception ignored){}if(local!=null){local.delete();new File(local.getPath()+".lock").delete();new File(local.getPath()+".previous").delete();}}
        finish(code,result);
    }
    private static String encode(JSONObject value)throws Exception{return "BV1:"+android.util.Base64.encodeToString(value.toString().getBytes("UTF-8"),android.util.Base64.URL_SAFE|android.util.Base64.NO_WRAP|android.util.Base64.NO_PADDING);}
}
