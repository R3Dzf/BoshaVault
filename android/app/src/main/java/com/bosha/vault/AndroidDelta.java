package com.bosha.vault;

import java.io.*;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.zip.GZIPInputStream;

/**
 * Lockvyn delta v1. Applies a small gzip compressed sequence of literal/copy
 * operations against the exact signed installed APK. The reconstructed APK
 * must match the published full APK SHA-256 before Android can install it.
 * No Android dependencies: also exercised against real synthetic fixtures in CI.
 */
final class AndroidDelta {
    static final long MAX_APK=64L*1024*1024;
    static final long MAX_PATCH=64L*1024*1024;
    private static final int MAGIC=0x4c564431; // LVD1
    interface Check { void alive() throws Exception; }
    private AndroidDelta(){}

    private static String hex(byte[] bytes){
        StringBuilder s=new StringBuilder(bytes.length*2);
        for(byte b:bytes)s.append(String.format(java.util.Locale.ROOT,"%02x",b&255));
        return s.toString();
    }
    static String sha256(File file)throws Exception {
        if(!file.isFile()||file.length()<1||file.length()>MAX_APK)
            throw new IOException("Installed APK is not available for a delta update.");
        MessageDigest sha=MessageDigest.getInstance("SHA-256");
        try(InputStream in=new BufferedInputStream(new FileInputStream(file))){
            byte[] buffer=new byte[32768];int n;
            while((n=in.read(buffer))!=-1)sha.update(buffer,0,n);
            Arrays.fill(buffer,(byte)0);
        }
        return hex(sha.digest());
    }
    static void apply(File source,File patch,File output,String expectedBase,
        String expectedTarget,long expectedLength,Check check)throws Exception {
        if(source.getCanonicalPath().equals(output.getCanonicalPath())||
           patch.getCanonicalPath().equals(output.getCanonicalPath()))
            throw new IOException("Delta output must not replace an input file.");
        if(source.length()<1||source.length()>MAX_APK||patch.length()<1||patch.length()>MAX_PATCH||
           expectedLength<1||expectedLength>MAX_APK)
            throw new IOException("Invalid delta input size.");
        if(!expectedBase.matches("[a-fA-F0-9]{64}")||!expectedTarget.matches("[a-fA-F0-9]{64}"))
            throw new IOException("Invalid patch identity.");
        check.alive();
        if(!sha256(source).equalsIgnoreCase(expectedBase))
            throw new IOException("Installed APK differs from the official base release.");
        MessageDigest sum=MessageDigest.getInstance("SHA-256");long written=0;
        boolean complete=false;
        try(RandomAccessFile old=new RandomAccessFile(source,"r");
            DataInputStream delta=new DataInputStream(new BufferedInputStream(new GZIPInputStream(new FileInputStream(patch))));
            FileOutputStream result=new FileOutputStream(output)){
            if(delta.readInt()!=MAGIC)throw new IOException("Invalid Lockvyn delta format.");
            byte[] baseHash=new byte[32],targetHash=new byte[32];
            delta.readFully(baseHash);delta.readFully(targetHash);
            if(!hex(baseHash).equalsIgnoreCase(expectedBase)||!hex(targetHash).equalsIgnoreCase(expectedTarget))
                throw new IOException("Patch does not match the installed or requested update.");
            if(delta.readLong()!=source.length()||delta.readLong()!=expectedLength)
                throw new IOException("Patch file size constraints did not match.");
            byte[] buffer=new byte[32768];
            int operations=0;
            while(written<expectedLength){
                check.alive();
                if(++operations>MAX_APK)throw new IOException("Too many patch operations.");
                int op=delta.readUnsignedByte();
                int length;
                if(op==0x43){ // 'C': copy from the immutable installed APK
                    long offset=delta.readLong();length=delta.readInt();
                    if(offset<0||length<=0||length>MAX_APK||offset>source.length()-length||
                       length>expectedLength-written)throw new IOException("Patch copy exceeds APK bounds.");
                    old.seek(offset);
                    int remain=length;
                    while(remain>0){
                        check.alive();int n=Math.min(remain,buffer.length);
                        old.readFully(buffer,0,n);result.write(buffer,0,n);sum.update(buffer,0,n);remain-=n;
                    }
                }else if(op==0x41){ // 'A': literal bytes added by the new release
                    length=delta.readInt();
                    if(length<=0||length>MAX_APK||length>expectedLength-written)
                        throw new IOException("Patch literal exceeds APK bounds.");
                    int remain=length;
                    while(remain>0){
                        check.alive();int n=Math.min(remain,buffer.length);
                        delta.readFully(buffer,0,n);result.write(buffer,0,n);sum.update(buffer,0,n);remain-=n;
                    }
                }else throw new IOException("Unknown delta operation.");
                written+=length;
            }
            if(delta.read()!=-1)throw new IOException("Trailing data in delta patch.");
            result.getFD().sync();
            Arrays.fill(buffer,(byte)0);
            if(!hex(sum.digest()).equalsIgnoreCase(expectedTarget))
                throw new IOException("Reconstructed APK checksum mismatch.");
            complete=true;
        }finally{if(!complete)output.delete();}
    }
}
