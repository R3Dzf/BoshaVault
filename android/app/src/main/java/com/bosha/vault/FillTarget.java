package com.bosha.vault;

import android.content.*;
import android.content.pm.*;
import android.view.autofill.AutofillId;
import android.os.Parcel;
import android.os.Parcelable;
import java.util.*;

final class FillTarget implements Parcelable {
    FillTarget(){}
    private FillTarget(Parcel in){
        pkg=in.readString();certificate=in.readString();domain=in.readString();scheme=in.readString();
        userDomain=in.readString();userScheme=in.readString();
        browser=in.readInt()!=0;explicitUsername=in.readInt()!=0;
        user=in.readParcelable(AutofillId.class.getClassLoader());
        password=in.readParcelable(AutofillId.class.getClassLoader());
        created=in.readLong();
    }
    @Override public void writeToParcel(Parcel out,int flags){
        out.writeString(pkg);out.writeString(certificate);out.writeString(domain);out.writeString(scheme);
        out.writeString(userDomain);out.writeString(userScheme);
        out.writeInt(browser?1:0);out.writeInt(explicitUsername?1:0);
        out.writeParcelable(user,flags);out.writeParcelable(password,flags);out.writeLong(created);
    }
    @Override public int describeContents(){return 0;}
    public static final Creator<FillTarget> CREATOR=new Creator<FillTarget>(){
        @Override public FillTarget createFromParcel(Parcel in){return new FillTarget(in);}
        @Override public FillTarget[] newArray(int size){return new FillTarget[size];}
    };

    String pkg,certificate,domain,scheme,userDomain,userScheme;boolean browser,explicitUsername;
    AutofillId user,password;long created=android.os.SystemClock.elapsedRealtime();
    static final Set<String> BROWSERS=new HashSet<>(Arrays.asList("com.android.chrome","com.chrome.beta","com.chrome.dev","com.microsoft.emmx","org.mozilla.firefox","com.brave.browser","com.transsion.phoenix"));
    static String certificate(Context c,String pkg)throws Exception{
        PackageInfo info=c.getPackageManager().getPackageInfo(pkg,PackageManager.GET_SIGNING_CERTIFICATES);android.content.pm.Signature[] sigs=info.signingInfo.getApkContentsSigners();
        if(sigs.length!=1)throw new Exception("Apps with multiple current signing certificates are not supported.");return VaultEngine.hex(VaultEngine.sha(sigs[0].toByteArray()));
    }
    void validate(Context c)throws Exception{
        if(android.os.SystemClock.elapsedRealtime()-created>180000)throw new Exception("Autofill request expired. Tap the field again.");
        if(!certificate.equals(certificate(c,pkg)))throw new Exception("The app's signing certificate changed. Autofill refused.");
        if(browser){
            String originHost=password!=null?domain:userDomain;
            String originScheme=password!=null?scheme:userScheme;
            if(originHost==null||!"https".equalsIgnoreCase(originScheme))
                throw new Exception("An HTTPS origin was not provided by this browser.");
            // The browser-reported origin, not text typed into an input, determines matching.
            String verified=VaultEngine.host("https://"+originHost);
            if(user!=null&&password!=null&&(!"https".equalsIgnoreCase(userScheme)||
                    userDomain==null||!VaultEngine.host("https://"+userDomain).equals(verified)))
                throw new Exception("Username and password fields have different origins.");
            domain=verified;scheme="https";
        } else if(domain!=null || userDomain!=null){
            throw new Exception("WebView autofill is not supported without a verified website association.");
        }
    }
}
