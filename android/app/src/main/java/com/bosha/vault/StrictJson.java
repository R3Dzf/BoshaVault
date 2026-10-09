package com.bosha.vault;

import java.util.*;
import org.json.*;

/** Validate RFC 8259 grammar, duplicates and depth before using Android's JSON API. */
final class StrictJson {
    private final String s; private int p;
    private StrictJson(String s) { this.s=s; }
    static JSONObject object(String s) throws Exception {
        StrictJson v=new StrictJson(s); v.value(0); v.ws();
        if(v.p!=s.length()) throw new Exception("Trailing data in vault.");
        return new JSONObject(s);
    }
    private void ws(){while(p<s.length()&&" \r\n\t".indexOf(s.charAt(p))>=0)p++;}
    private char at(){return p<s.length()?s.charAt(p):'\0';}
    private void need(char c)throws Exception{ws();if(at()!=c)bad();p++;}
    private void bad()throws Exception{throw new Exception("Invalid vault JSON.");}
    private String string()throws Exception{
        ws();int start=p;need('"');
        while(p<s.length()){
            char c=s.charAt(p++);
            if(c=='"')return (String)new JSONTokener(s.substring(start,p)).nextValue();
            if(c<32)bad();
            if(c=='\\'){
                if(p>=s.length())bad();char e=s.charAt(p++);
                if(e=='u'){for(int i=0;i<4;i++){if(p>=s.length()||Character.digit(s.charAt(p++),16)<0)bad();}}
                else if("\"\\/bfnrt".indexOf(e)<0)bad();
            }
        }bad();return "";
    }
    private void value(int depth)throws Exception{
        if(depth>16)bad();ws();char c=at();
        if(c=='{'){
            p++;ws();Set<String> keys=new HashSet<>();if(at()=='}'){p++;return;}
            while(true){String k=string();if(!keys.add(k))throw new Exception("Duplicate JSON property.");need(':');value(depth+1);ws();if(at()=='}'){p++;return;}need(',');}
        }else if(c=='['){p++;ws();if(at()==']'){p++;return;}while(true){value(depth+1);ws();if(at()==']'){p++;return;}need(',');}}
        else if(c=='"')string();
        else if(s.startsWith("true",p))p+=4;else if(s.startsWith("false",p))p+=5;else if(s.startsWith("null",p))p+=4;
        else {
            int start=p;if(at()=='-')p++;if(at()=='0')p++;else{if(at()<'1'||at()>'9')bad();while(at()>='0'&&at()<='9')p++;}
            if(at()=='.'){p++;int a=p;while(at()>='0'&&at()<='9')p++;if(p==a)bad();}
            if(at()=='e'||at()=='E'){p++;if(at()=='+'||at()=='-')p++;int a=p;while(at()>='0'&&at()<='9')p++;if(a==p)bad();}
            if(start==p)bad();
        }
    }
    static void keys(JSONObject o,String...allowed)throws Exception{
        Set<String> a=new HashSet<>(Arrays.asList(allowed));Iterator<String> it=o.keys();while(it.hasNext())if(!a.contains(it.next()))throw new Exception("Unsupported vault property.");
        if(o.length()!=a.size())throw new Exception("Missing vault property.");
    }
    static long number(JSONObject o,String key)throws Exception{
        Object n=o.get(key);if(!(n instanceof Number)||n instanceof Double||n instanceof Float)throw new Exception("Invalid numeric field.");return ((Number)n).longValue();
    }
}
