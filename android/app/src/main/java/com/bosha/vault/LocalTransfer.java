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
    interface SocketBinder { void bind(Socket socket,String host)throws IOException; }
    interface Progress { void show(String message); }
    private final String host,token,pin;
    private final int port;
    private final SocketBinder binder;
    private final Progress progress;
    private volatile Socket activeSocket;
    private volatile boolean cancelled;
    private boolean receipts;

    LocalTransfer(String pairing)throws Exception{this(pairing,(socket,host)->{},message->{});}
    LocalTransfer(String pairing,SocketBinder binder,Progress progress)throws Exception{
        this.binder=binder;this.progress=progress;
        if(pairing==null||!pairing.startsWith("BV1:")||pairing.length()>4096)throw new Exception("Invalid pairing code.");
        JSONObject p=StrictJson.object(VaultEngine.utf8(Base64.getUrlDecoder().decode(pairing.substring(4))));
        StrictJson.keys(p,"v","host","port","cert","token");
        if(StrictJson.number(p,"v")!=1)throw new Exception("Unsupported pairing code.");
        host=p.getString("host");long candidatePort=StrictJson.number(p,"port");pin=p.getString("cert");token=p.getString("token");
        if(!host.matches("[0-9]{1,3}(\\.[0-9]{1,3}){3}")||candidatePort<1024||candidatePort>65535||!pin.matches("[a-f0-9]{64}")||!token.matches("[A-Za-z0-9_-]{43}")||Base64.getUrlDecoder().decode(token).length!=32)throw new Exception("Invalid pairing details.");
        port=(int)candidatePort;
        String[] parts=host.split("\\.");int[] ip=new int[4];
        for(int i=0;i<4;i++){ip[i]=Integer.parseInt(parts[i]);if(ip[i]>255||!parts[i].equals(Integer.toString(ip[i])))throw new Exception("Invalid IP address.");}
        if(!(ip[0]==10||ip[0]==192&&ip[1]==168||ip[0]==172&&ip[1]>=16&&ip[1]<=31))throw new Exception("Transfer is restricted to private Wi-Fi addresses.");
    }
    synchronized void cancel(){cancelled=true;if(activeSocket!=null)try{activeSocket.close();}catch(IOException ignored){}}
    private synchronized void activate(Socket socket)throws IOException{if(cancelled)throw new Cancelled();activeSocket=socket;}
    private void ensureActive()throws Cancelled{if(cancelled)throw new Cancelled();}
    byte[] download()throws Exception{return exchange("GET",new byte[0],3);}
    void upload(byte[] bytes)throws Exception{
        if(bytes==null||bytes.length<100||bytes.length>VaultEngine.MAX_BYTES)throw new IOException("Invalid encrypted upload size.");
        byte[] response=exchange("POST",bytes,receipts?3:1);
        if(!Arrays.equals(response,"Merged".getBytes(StandardCharsets.UTF_8)))throw new IOException("Windows did not confirm the merge. Your phone vault is retained.");
    }
    private byte[] exchange(String method,byte[] body,int attempts)throws Exception{
        for(int attempt=1;;attempt++){
            ensureActive();progress.show(attempt==1?"Connecting over local Wi-Fi…":"Connection interrupted. Retrying local Wi-Fi ("+attempt+"/"+attempts+")…");
            try{return request(method,body);}
            catch(TransientFailure failure){if(attempt>=attempts)throw failure;ensureActive();Thread.sleep(200);}
        }
    }
    private byte[] request(String method,byte[] body)throws Exception{
        TrustManager[] trust={new X509TrustManager(){
            public X509Certificate[]getAcceptedIssuers(){return new X509Certificate[0];}
            public void checkClientTrusted(X509Certificate[]c,String s)throws java.security.cert.CertificateException{throw new java.security.cert.CertificateException("No client certificates.");}
            public void checkServerTrusted(X509Certificate[]c,String s)throws java.security.cert.CertificateException{
                try{if(c.length!=1||!MessageDigest.isEqual(VaultEngine.sha(c[0].getEncoded()),hexDecode(pin)))throw new Exception("Certificate pin mismatch.");c[0].checkValidity();}
                catch(Exception e){throw new java.security.cert.CertificateException("Pairing certificate verification failed.");}
            }
        }};
        SSLContext context=SSLContext.getInstance("TLS");context.init(null,trust,new SecureRandom());
        String stage="connecting to Windows";
        Socket plain=new Socket();
        try(Socket transport=plain){
            activate(transport);
            // Bind the unconnected plain socket, then layer TLS over it. Android's
            // default route may be cellular when this Wi-Fi has no internet.
            binder.bind(transport,host);ensureActive();
            transport.connect(new InetSocketAddress(InetAddress.getByName(host),port),8000);
            try(SSLSocket socket=(SSLSocket)context.getSocketFactory().createSocket(transport,host,port,true)){
                socket.setSoTimeout(method.equals("POST")?60000:20000);
                List<String> protocols=new ArrayList<>();
                for(String p:socket.getSupportedProtocols())if(p.equals("TLSv1.2")||p.equals("TLSv1.3"))protocols.add(p);
                socket.setEnabledProtocols(protocols.toArray(new String[0]));
                stage="TLS certificate verification";progress.show("Verifying your Windows pairing certificate…");socket.startHandshake();ensureActive();
                stage="sending encrypted data";progress.show(method.equals("GET")?"Receiving the encrypted Windows vault…":"Sending the merged encrypted vault. Check any prompt on Windows…");
                OutputStream out=socket.getOutputStream();
                String h=method+" /vault HTTP/1.1\r\nHost: "+host+"\r\nAuthorization: Bearer "+token+"\r\nContent-Length: "+body.length+"\r\nConnection: close\r\n\r\n";
                out.write(h.getBytes(StandardCharsets.US_ASCII));out.write(body);out.flush();
                stage="reading the Windows response";
                InputStream in=socket.getInputStream();ByteArrayOutputStream headers=new ByteArrayOutputStream();int tail=0;
                while(headers.size()<4096){int b=in.read();if(b<0)throw new EOFException();headers.write(b);tail=(tail<<8)|b;if(tail==0x0D0A0D0A)break;}
                if(tail!=0x0D0A0D0A)throw new IOException("Invalid transfer response.");
                String[] lines=headers.toString("US-ASCII").split("\r\n");
                if(!lines[0].equals("HTTP/1.1 200 OK"))throw new IOException("Windows rejected the request. Check its prompt, or start a new transfer and scan its current QR. Your phone vault is retained.");
                Map<String,String> fields=new HashMap<>();
                for(int i=1;i<lines.length;i++){int sep=lines[i].indexOf(':');if(sep<1||fields.put(lines[i].substring(0,sep).trim().toLowerCase(Locale.ROOT),lines[i].substring(sep+1).trim())!=null)throw new IOException("Invalid transfer response.");}
                int size=Integer.parseInt(fields.getOrDefault("content-length","-1"));
                if(size<0||size>(method.equals("POST")?128:VaultEngine.MAX_BYTES)||fields.containsKey("transfer-encoding"))throw new IOException("Invalid transfer size.");
                byte[] bytes=new byte[size];int offset=0;
                try{while(offset<size){ensureActive();int n=in.read(bytes,offset,size-offset);if(n<0)throw new EOFException();offset+=n;}}
                catch(Exception e){Arrays.fill(bytes,(byte)0);throw e;}
                if(method.equals("GET"))receipts="2".equals(fields.get("x-lockvyn-transfer"));
                ensureActive();return bytes;
            }
        }
        catch(SSLHandshakeException e){ensureActive();throw new IOException("Pairing certificate verification failed. Scan a new QR and check automatic date/time on both devices.",e);}
        catch(SocketTimeoutException|ConnectException|NoRouteToHostException e){ensureActive();throw new TransientFailure("Cannot reach Windows during "+stage+". Keep its transfer window open, select the current Wi-Fi IPv4 and allow Lockvyn on the Windows private network. Guest Wi-Fi can block devices.",e);}
        catch(SSLException|EOFException|SocketException e){ensureActive();throw new TransientFailure("Connection interrupted during "+stage+". Local data is retained. Check the current QR and Windows transfer status.",e);}
        finally{synchronized(this){if(activeSocket==plain)activeSocket=null;}}
    }
    private static final class TransientFailure extends IOException{TransientFailure(String message,Exception cause){super(message,cause);}}
    private static final class Cancelled extends IOException{Cancelled(){super("Transfer cancelled. Local vault data is retained.");}}
    private static byte[] hexDecode(String s){byte[] b=new byte[32];for(int i=0;i<32;i++)b[i]=(byte)Integer.parseInt(s.substring(i*2,i*2+2),16);return b;}
}
