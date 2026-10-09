package com.bosha.vault;

import java.io.*;
import java.net.*;
import java.nio.charset.StandardCharsets;
import java.security.*;
import java.security.cert.X509Certificate;
import java.util.*;
import javax.net.ssl.*;
import org.json.JSONObject;

final class LocalTransfer {
    private final String host,token,pin; private final int port;
    LocalTransfer(String pairing)throws Exception{
        if(!pairing.startsWith("BV1:")||pairing.length()>4096)throw new Exception("Invalid pairing code.");JSONObject p=StrictJson.object(VaultEngine.utf8(Base64.getUrlDecoder().decode(pairing.substring(4))));StrictJson.keys(p,"v","host","port","cert","token");
        if(StrictJson.number(p,"v")!=1)throw new Exception("Unsupported pairing code.");host=p.getString("host");port=(int)StrictJson.number(p,"port");pin=p.getString("cert");token=p.getString("token");
        if(!host.matches("[0-9]{1,3}(\\.[0-9]{1,3}){3}")||port<1024||port>65535||!pin.matches("[a-f0-9]{64}")||!token.matches("[A-Za-z0-9_-]{43}")||Base64.getUrlDecoder().decode(token).length!=32)throw new Exception("Invalid pairing details.");
        String[] parts=host.split("\\.");int[] ip=new int[4];for(int i=0;i<4;i++){ip[i]=Integer.parseInt(parts[i]);if(ip[i]>255||!parts[i].equals(Integer.toString(ip[i])))throw new Exception("Invalid IP address.");}
        if(!(ip[0]==10||ip[0]==192&&ip[1]==168||ip[0]==172&&ip[1]>=16&&ip[1]<=31))throw new Exception("Transfer is restricted to private Wi-Fi addresses.");
    }
    byte[] download()throws Exception{return request("GET",new byte[0]);}
    void upload(byte[] bytes)throws Exception{request("POST",bytes);}
    private byte[] request(String method,byte[] body)throws Exception{
        TrustManager[] trust={new X509TrustManager(){public X509Certificate[]getAcceptedIssuers(){return new X509Certificate[0];}public void checkClientTrusted(X509Certificate[]c,String s)throws java.security.cert.CertificateException{throw new java.security.cert.CertificateException("No client certificates.");}public void checkServerTrusted(X509Certificate[]c,String s)throws java.security.cert.CertificateException{try{if(c.length!=1||!MessageDigest.isEqual(VaultEngine.sha(c[0].getEncoded()),hexDecode(pin)))throw new Exception("Certificate pin mismatch.");c[0].checkValidity();}catch(Exception e){throw new java.security.cert.CertificateException("Pairing certificate verification failed.");}}}};
        SSLContext context=SSLContext.getInstance("TLS");context.init(null,trust,new SecureRandom());
        // Display a safe stage-specific error; never show/store pairing tokens or encrypted payloads.
        String stage="connecting to the Windows listener";
        try(SSLSocket socket=(SSLSocket)context.getSocketFactory().createSocket()){
            socket.connect(new InetSocketAddress(InetAddress.getByName(host),port),8000);
            socket.setSoTimeout(20000);
            List<String> protocols=new ArrayList<>();
            for(String p:socket.getSupportedProtocols())if(p.equals("TLSv1.2")||p.equals("TLSv1.3"))protocols.add(p);
            socket.setEnabledProtocols(protocols.toArray(new String[0]));
            stage="TLS certificate verification";
            socket.startHandshake();
            stage="sending the encrypted transfer request";
            OutputStream out=socket.getOutputStream();
            String h=method+" /vault HTTP/1.1\r\nHost: "+host+"\r\nAuthorization: Bearer "+token+"\r\nContent-Length: "+body.length+"\r\nConnection: close\r\n\r\n";
            out.write(h.getBytes(StandardCharsets.US_ASCII));out.write(body);out.flush();
            stage="reading the Windows transfer response";
            InputStream in=socket.getInputStream();ByteArrayOutputStream headers=new ByteArrayOutputStream();int tail=0;
            while(headers.size()<4096){int b=in.read();if(b<0)throw new EOFException("Connection closed before the Windows response.");headers.write(b);tail=(tail<<8)|b;if(tail==0x0D0A0D0A)break;}
            if(tail!=0x0D0A0D0A)throw new Exception("Invalid transfer response.");
            String[] lines=headers.toString("US-ASCII").split("\r\n");
            if(!lines[0].equals("HTTP/1.1 200 OK"))throw new Exception("Windows rejected this request. Start another transfer and scan its latest QR.");
            Map<String,String> fields=new HashMap<>();
            for(int i=1;i<lines.length;i++){int sep=lines[i].indexOf(':');if(sep<1||fields.put(lines[i].substring(0,sep).toLowerCase(Locale.ROOT),lines[i].substring(sep+1).trim())!=null)throw new Exception("Invalid transfer response.");}
            int size=Integer.parseInt(fields.getOrDefault("content-length","-1"));
            if(size<0||size>VaultEngine.MAX_BYTES||fields.containsKey("transfer-encoding"))throw new Exception("Invalid transfer size.");
            byte[] bytes=new byte[size];int offset=0;
            while(offset<size){int n=in.read(bytes,offset,size-offset);if(n<0)throw new EOFException("Windows closed before completing the encrypted response.");offset+=n;}
            return bytes;
        }
        catch(SocketTimeoutException e){throw new IOException("Timeout during "+stage+". Keep the Windows transfer window open and check the private Wi-Fi connection.",e);}
        catch(ConnectException|NoRouteToHostException e){throw new IOException("Cannot reach the Windows transfer listener. Check the selected Wi-Fi IPv4, firewall and current QR.",e);}
        catch(SSLHandshakeException e){throw new IOException("TLS handshake failed. Start a new transfer, scan its new QR, and check the certificate/error on Windows.",e);}
        catch(SSLException e){throw new IOException("Secure connection interrupted during "+stage+". Check the transfer.log on Windows.",e);}
        catch(EOFException e){throw new IOException("Windows closed the connection during "+stage+". Check transfer.log, then retry with a new QR if needed.",e);}
        catch(SocketException e){throw new IOException("Connection closed during "+stage+". Check transfer.log on Windows and verify the same private Wi-Fi.",e);}
    }
    private static byte[] hexDecode(String s){byte[] b=new byte[32];for(int i=0;i<32;i++)b[i]=(byte)Integer.parseInt(s.substring(i*2,i*2+2),16);return b;}
}
