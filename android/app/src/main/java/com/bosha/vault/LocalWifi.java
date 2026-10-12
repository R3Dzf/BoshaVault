package com.bosha.vault;

import android.content.Context;
import android.net.*;
import java.io.IOException;
import java.net.*;
import java.util.*;

/** Only the transfer socket is routed to local Wi-Fi. No process-wide routing changes. */
final class LocalWifi {
    static LocalTransfer connect(Context context,String pairing)throws Exception{return new LocalTransfer(pairing,(socket,host)->bind(context,socket,host),message->{});}
    static int score(NetworkCapabilities caps,LinkProperties links,InetAddress target){
        if(caps==null||links==null)return -1;
        List<LinkAddress> rows=links.getLinkAddresses();byte[][] addresses=new byte[rows.size()][];int[] prefixes=new int[rows.size()];
        for(int i=0;i<rows.size();i++){addresses[i]=rows.get(i).getAddress().getAddress();prefixes[i]=rows.get(i).getPrefixLength();}
        return score(caps.hasTransport(NetworkCapabilities.TRANSPORT_WIFI),caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN),addresses,prefixes,target.getAddress());
    }
    static int score(boolean wifi,boolean vpn,byte[][] addresses,int[] prefixes,byte[] target){
        if(!wifi||vpn||addresses==null||prefixes==null||addresses.length!=prefixes.length)return -1;
        int score=0;
        for(int i=0;i<addresses.length;i++)if(addresses[i].length==4){score=Math.max(score,sameSubnet(addresses[i],target,prefixes[i])?2:1);}
        return score;
    }
    static void bind(Context context,Socket socket,String host)throws IOException{
        ConnectivityManager manager=(ConnectivityManager)context.getSystemService(Context.CONNECTIVITY_SERVICE);
        if(manager==null)throw new IOException("Wi-Fi is unavailable. Connect both devices to the same local Wi-Fi.");
        InetAddress target=InetAddress.getByName(host);Network chosen=null;int best=-1;
        for(Network network:manager.getAllNetworks()){
            NetworkCapabilities caps=manager.getNetworkCapabilities(network);LinkProperties links=manager.getLinkProperties(network);
            // Offline Wi-Fi is eligible: INTERNET/VALIDATED are deliberately not required.
            int score=score(caps,links,target);
            if(score>best&&score>0){best=score;chosen=network;}
        }
        if(chosen!=null){
            try{chosen.bindSocket(socket);}catch(IOException failure){throw new IOException("Android blocked the local Wi-Fi route. Reconnect Wi-Fi and check whether a VPN permits local-network access.",failure);}return;
        }
        // A phone hosting a hotspot may expose its AP interface without a Wi-Fi
        // Network handle. Bind its local source IPv4 only when the PC shares that
        // subnet; never fall back to an arbitrary mobile-data/default socket.
        Enumeration<NetworkInterface> interfaces=NetworkInterface.getNetworkInterfaces();
        if(interfaces!=null)while(interfaces.hasMoreElements()){
            NetworkInterface network=interfaces.nextElement();String name=network.getName().toLowerCase(Locale.ROOT);
            if(!network.isUp()||network.isLoopback()||!(name.startsWith("wlan")||name.startsWith("ap")||name.startsWith("swlan")))continue;
            for(InterfaceAddress link:network.getInterfaceAddresses()){
                InetAddress local=link.getAddress();
                if(local instanceof Inet4Address&&sameSubnet(local.getAddress(),target.getAddress(),link.getNetworkPrefixLength())){
                    socket.bind(new InetSocketAddress(local,0));return;
                }
            }
        }
        throw new IOException("No local Wi-Fi route is available. Connect both devices to the same Wi-Fi or hotspot, then retry. Internet and cloud login are not required.");
    }
    static boolean sameSubnet(byte[] local,byte[] target,int prefix){
        if(local.length!=4||target.length!=4||prefix<1||prefix>32)return false;
        for(int i=0;i<4;i++){int bits=Math.min(8,Math.max(0,prefix-i*8));int mask=(0xff<<(8-bits))&0xff;if((local[i]&mask)!=(target[i]&mask))return false;}
        return true;
    }
}
