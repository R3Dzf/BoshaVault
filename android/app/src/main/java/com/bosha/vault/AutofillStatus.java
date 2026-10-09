package com.bosha.vault;

import android.content.*;
import android.os.Build;
import android.view.autofill.AutofillManager;
import java.text.DateFormat;
import java.util.Date;

// Stores only a bounded status code and timestamp. Never save target apps,
// websites, input contents, usernames, or credentials in diagnostics.
final class AutofillStatus {
    private static final String PREFS="autofill-status-v1";
    static void report(Context context,String code) {
        if(!code.matches("SERVICE_CALLED|NO_CONTEXT|NO_ACTIVITY|NO_CREDENTIAL_FIELD|UNVERIFIED_DESTINATION|PACKAGE_NOT_VISIBLE|AMBIGUOUS_FORM|TOO_COMPLEX|BUSY|READY|AUTH_OPENED|AUTH_UNLOCKED|AUTH_DELIVERED|AUTH_FAILED|INTERNAL_ERROR"))code="INTERNAL_ERROR";
        context.getSharedPreferences(PREFS,Context.MODE_PRIVATE).edit()
                .putString("code",code).putLong("time",System.currentTimeMillis()).apply();
    }
    static boolean isEnabled(Context context) {
        AutofillManager manager=context.getSystemService(AutofillManager.class);
        if(manager==null||!manager.isAutofillSupported())return false;
        if(Build.VERSION.SDK_INT>=28){
            ComponentName selected=manager.getAutofillServiceComponentName();
            return selected!=null&&selected.getPackageName().equals(context.getPackageName())
                    &&selected.getClassName().equals(VaultAutofillService.class.getName());
        }
        return manager.hasEnabledAutofillServices();
    }
    static String summary(Context context) {
        if(context.getSystemService(AutofillManager.class)==null)return "Autofill is unavailable on this Android device.";
        if(!isEnabled(context))return "Status: Not selected. Tap Enable Android Autofill and select BoshaVault as the autofill provider.";
        return "Status: BoshaVault is selected as the Android autofill provider.";
    }
    static String diagnostic(Context context) {
        android.content.SharedPreferences p=context.getSharedPreferences(PREFS,Context.MODE_PRIVATE);
        String code=p.getString("code","NEVER_CALLED");
        String detail;
        switch(code){
            case "NO_CONTEXT":detail="Android provided no usable form context.";break;
            case "NO_ACTIVITY":detail="Android did not identify the requesting app.";break;
            case "NO_CREDENTIAL_FIELD":detail="No recognized login field. The website/app may not expose Autofill hints.";break;
            case "UNVERIFIED_DESTINATION":detail="The destination could not be verified (HTTPS origin, browser identity, or app signature). Nothing was filled.";break;
            case "PACKAGE_NOT_VISIBLE":detail="Android did not expose the target app's signing identity. Autofill refused for safety.";break;
            case "AMBIGUOUS_FORM":detail="The form had multiple or conflicting credential fields. Autofill refused for safety.";break;
            case "TOO_COMPLEX":detail="The form was too complex to analyze safely.";break;
            case "BUSY":detail="Too many pending unlock requests. Tap a field again.";break;
            case "READY":detail="Android was given a BoshaVault suggestion. Whether it is shown depends on the target app and keyboard.";break;
            case "SERVICE_CALLED":detail="The service received the request but did not produce a suggestion.";break;
            case "INTERNAL_ERROR":detail="The service hit an unexpected failure. No private form data was recorded.";break;
            default:detail="Autofill state: "+code;break;
        }
        long time=p.getLong("time",0L);
        return "Last request: "+(time==0L?"never":DateFormat.getDateTimeInstance(DateFormat.SHORT,DateFormat.SHORT).format(new Date(time)))
                +"\n"+detail+"\nOnly status codes and timestamps are saved locally.";
    }
    static void clear(Context c){c.getSharedPreferences(PREFS,Context.MODE_PRIVATE).edit().clear().apply();}
}
