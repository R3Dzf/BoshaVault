package com.bosha.vault;

import android.content.*;
import android.content.pm.*;
import android.view.autofill.AutofillId;
import java.util.*;

final class FillTarget {
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
