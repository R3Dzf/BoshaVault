package com.bosha.vault;

import java.security.SecureRandom;
import java.util.*;

final class PasswordOptions {
    static final String DEFAULT_SYMBOLS="!@#$%&*+-=?_";
    int length=24,minimumDigits=1,minimumSymbols=1;
    boolean lowercase=true,uppercase=true,digits=true,symbols=true,excludeAmbiguous=true;
    String allowedSymbols=DEFAULT_SYMBOLS;
    private static final SecureRandom RANDOM=new SecureRandom();
    private static final class Group {final String alphabet;final int minimum;Group(String a,int m){alphabet=a;minimum=m;}}
    private List<Group> groups()throws Exception{
        if(length<8||length>128)throw new Exception("Use a password length of 8–128.");
        if(allowedSymbols==null||allowedSymbols.length()>32)throw new Exception("Use up to 32 allowed punctuation characters.");
        for(char c:allowedSymbols.toCharArray())if(c<'!'||c>'~'||Character.isLetterOrDigit(c))throw new Exception("Allowed symbols must be punctuation, without spaces.");
        List<Group> out=new ArrayList<>();
        if(lowercase)out.add(new Group(excludeAmbiguous?"abcdefghijkmnopqrstuvwxyz":"abcdefghijklmnopqrstuvwxyz",1));
        if(uppercase)out.add(new Group(excludeAmbiguous?"ABCDEFGHJKLMNPQRSTUVWXYZ":"ABCDEFGHIJKLMNOPQRSTUVWXYZ",1));
        if(digits){if(minimumDigits<1||minimumDigits>128)throw new Exception("Require at least 1–128 digits when enabled.");out.add(new Group(excludeAmbiguous?"23456789":"0123456789",minimumDigits));}
        if(symbols){if(minimumSymbols<1||minimumSymbols>128||allowedSymbols.isEmpty())throw new Exception("Choose symbols and require at least 1–128 when enabled.");StringBuilder unique=new StringBuilder();for(char c:allowedSymbols.toCharArray())if(unique.indexOf(String.valueOf(c))<0)unique.append(c);out.add(new Group(unique.toString(),minimumSymbols));}
        if(out.isEmpty())throw new Exception("Enable at least one character group.");
        int required=0;for(Group g:out)required+=g.minimum;if(required>length)throw new Exception("Required character counts exceed the password length.");return out;
    }
    void validate()throws Exception{groups();}
    String generate()throws Exception{
        List<Group> groups=groups();StringBuilder alphabet=new StringBuilder();char[] out=new char[length];int next=0;
        for(Group group:groups){alphabet.append(group.alphabet);for(int n=0;n<group.minimum;n++)out[next++]=group.alphabet.charAt(RANDOM.nextInt(group.alphabet.length()));}
        while(next<length)out[next++]=alphabet.charAt(RANDOM.nextInt(alphabet.length()));
        for(int i=length-1;i>0;i--){int j=RANDOM.nextInt(i+1);char c=out[i];out[i]=out[j];out[j]=c;}return new String(out);
    }
}
