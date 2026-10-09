package com.bosha.vault;

import android.content.*;
import android.content.pm.*;
import android.view.autofill.AutofillId;
import java.util.*;

final class FillTarget {
    String pkg,certificate,domain,scheme,userDomain,userScheme;boolean browser;
    AutofillId user,password;long created=android.os.SystemClock.elapsedRealtime();
    static final Set<String> BROWSERS=new HashSet<>(Arrays.asList("com.android.chrome","com.chrome.beta","com.chrome.dev","com.microsoft.emmx","org.mozilla.firefox","com.brave.browser"));
    static String certificate(Context c,String pkg)throws Exception{
        PackageInfo info=c.getPackageManager().getPackageInfo(pkg,PackageManager.GET_SIGNING_CERTIFICATES);android.content.pm.Signature[] sigs=info.signingInfo.getApkContentsSigners();
        if(sigs.length!=1)throw new Exception("Apps with multiple current signing certificates are not supported.");return VaultEngine.hex(VaultEngine.sha(sigs[0].toByteArray()));
    }
    void validate(Context c)throws Exception{
        if(android.os.SystemClock.elapsedRealtime()-created>180000)throw new Exception("Autofill request expired. Tap the field again.");
        if(!certificate.equals(certificate(c,pkg)))throw new Exception("The app's signing certificate changed. Autofill refused.");
        if(browser){if(domain==null||!"https".equals(scheme))throw new Exception("An HTTPS origin was not provided by this browser.");VaultEngine.host("https://"+domain);if(user!=null&&(!"https".equals(userScheme)||userDomain==null||!VaultEngine.host("https://"+userDomain).equals(VaultEngine.host("https://"+domain))))throw new Exception("Username and password fields have different origins.");}
        else if(domain!=null)throw new Exception("WebView autofill is not supported without a verified website association.");
    }
}
