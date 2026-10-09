package com.bosha.vault;

import android.app.*;
import android.app.assist.AssistStructure;
import android.content.*;
import android.content.pm.PackageManager;
import android.os.*;
import android.service.autofill.*;
import android.text.InputType;
import android.util.Pair;
import android.view.View;
import android.view.ViewStructure;
import android.view.autofill.*;
import android.widget.RemoteViews;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;

public final class VaultAutofillService extends AutofillService {
    // Expiring one-use references only; no credentials or form text live here.
    static final Map<String,FillTarget> REQUESTS=new ConcurrentHashMap<>();
    private int nodes;
    @Override public void onFillRequest(FillRequest request,CancellationSignal cancel,FillCallback callback){
        AutofillStatus.report(this,"SERVICE_CALLED");
        try{
            if(cancel.isCanceled())return;
            if(request.getFillContexts().isEmpty()){AutofillStatus.report(this,"NO_CONTEXT");callback.onSuccess(null);return;}
            AssistStructure structure=request.getFillContexts().get(request.getFillContexts().size()-1).getStructure();
            if(structure.getActivityComponent()==null){AutofillStatus.report(this,"NO_ACTIVITY");callback.onSuccess(null);return;}
            FillTarget target=new FillTarget();
            target.pkg=structure.getActivityComponent().getPackageName();
            if(target.pkg.equals(getPackageName())){callback.onSuccess(null);return;}
            target.certificate=FillTarget.certificate(this,target.pkg);
            target.browser=FillTarget.BROWSERS.contains(target.pkg);
            nodes=0;
            for(int i=0;i<structure.getWindowNodeCount();i++)
                scan(structure.getWindowNodeAt(i).getRootViewNode(),target,null,null,0);
            // Support explicit username-only login steps, but not generic email
            // newsletter forms. No credential data is read until authentication.
            if(target.password==null && !(target.user!=null && target.explicitUsername)){
                AutofillStatus.report(this,"NO_CREDENTIAL_FIELD");
                callback.onSuccess(null);return;
            }
            target.validate(this);
            REQUESTS.entrySet().removeIf(x->SystemClock.elapsedRealtime()-x.getValue().created>180000);
            if(REQUESTS.size()>=8){AutofillStatus.report(this,"BUSY");callback.onSuccess(null);return;}
            String nonce=UUID.randomUUID().toString();
            REQUESTS.put(nonce,target);
            Intent intent=new Intent(this,FillActivity.class).putExtra("request",nonce);
            PendingIntent pending=PendingIntent.getActivity(this,nonce.hashCode(),intent,PendingIntent.FLAG_CANCEL_CURRENT|PendingIntent.FLAG_IMMUTABLE);
            List<AutofillId> ids=new ArrayList<>();
            if(target.user!=null)ids.add(target.user);
            if(target.password!=null)ids.add(target.password);
            RemoteViews label=new RemoteViews(getPackageName(),android.R.layout.simple_list_item_1);
            label.setTextViewText(android.R.id.text1,"Fill with BoshaVault");
            if(cancel.isCanceled()){REQUESTS.remove(nonce);return;}
            callback.onSuccess(new FillResponse.Builder()
                    .setAuthentication(ids.toArray(new AutofillId[0]),pending.getIntentSender(),label)
                    .build());
            AutofillStatus.report(this,"READY");
        }catch(PackageManager.NameNotFoundException e){
            AutofillStatus.report(this,"PACKAGE_NOT_VISIBLE");callback.onSuccess(null);
        }catch(Exception e){
            String message=e.getMessage()==null?"":e.getMessage();
            String reason=message.contains("Ambiguous")?"AMBIGUOUS_FORM":
                    message.contains("too large")||message.contains("too deep")?"TOO_COMPLEX":
                    message.contains("origin")||message.contains("certificate")||message.contains("hostname")||
                    message.contains("WebView")||message.contains("HTTPS")||message.contains("signing")?"UNVERIFIED_DESTINATION":
                    "INTERNAL_ERROR";
            AutofillStatus.report(this,reason);
            callback.onSuccess(null);
        }
    }
    // High-confidence recognition of login fields from Android Autofill hints,
    // native input types, and HTML autocomplete semantics. No text is extracted.
    private void scan(AssistStructure.ViewNode n,FillTarget t,String domain,String scheme,int depth)throws Exception{
        if(++nodes>2000||depth>32)throw new Exception("Form too large.");
        if(n.getWebDomain()!=null){domain=n.getWebDomain();scheme=n.getWebScheme();}
        int type=n.getInputType(),variation=type&InputType.TYPE_MASK_VARIATION;
        boolean password=(type&InputType.TYPE_MASK_CLASS)==InputType.TYPE_CLASS_TEXT&&
                (variation==InputType.TYPE_TEXT_VARIATION_PASSWORD ||
                 variation==InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD ||
                 variation==InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD);
        boolean username=(type&InputType.TYPE_MASK_CLASS)==InputType.TYPE_CLASS_TEXT&&
                (variation==InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS ||
                 variation==InputType.TYPE_TEXT_VARIATION_WEB_EMAIL_ADDRESS);
        boolean explicitUsername=false;
        String[] hints=n.getAutofillHints();
        if(hints!=null)for(String hint:hints){
            if(hint==null)continue;
            String h=hint.toLowerCase(Locale.ROOT);
            if(h.equals(View.AUTOFILL_HINT_PASSWORD))password=true;
            if(h.equals(View.AUTOFILL_HINT_USERNAME)){username=true;explicitUsername=true;}
            if(h.equals(View.AUTOFILL_HINT_EMAIL_ADDRESS))username=true;
        }
        String id=n.getIdEntry();
        if(id!=null){
            String idLower=id.toLowerCase(Locale.ROOT).replace("-","_");
            if(idLower.equals("username")||idLower.equals("user_name")||
                    idLower.equals("login")||idLower.equals("login_email")||
                    idLower.equals("email")||idLower.equals("user_email"))username=true;
        }
        ViewStructure.HtmlInfo html=n.getHtmlInfo();
        boolean exclude=false;
        if(html!=null){
            List<Pair<String,String>> attributes=html.getAttributes();
            if(attributes!=null)for(Pair<String,String> pair:attributes){
                if(pair.first==null || pair.second==null)continue;
                String key=pair.first.toLowerCase(Locale.ROOT);
                String val=pair.second.toLowerCase(Locale.ROOT).trim();
                if(key.equals("autocomplete")){
                    if(val.contains("new-password")||val.contains("one-time-code")||val.contains("cc-"))exclude=true;
                    if(val.contains("current-password"))password=true;
                    if(val.matches("(^|.*\\s)username(\\s.*|$)")){username=true;explicitUsername=true;}
                    if(val.matches("(^|.*\\s)email(\\s.*|$)"))username=true;
                }
                if(key.equals("type")){
                    if(val.equals("password"))password=true;
                    if(val.equals("email"))username=true;
                }
            }
        }
        if(!exclude && n.getVisibility()==View.VISIBLE && n.isEnabled() && n.getAutofillId()!=null){
            // Only one password field and one username field per login form.
            if(password){
                if(t.password!=null&&!t.password.equals(n.getAutofillId()))throw new Exception("Ambiguous password fields.");
                t.password=n.getAutofillId();t.domain=domain;t.scheme=scheme;
            }
            if(username){
                if(t.user!=null&&!t.user.equals(n.getAutofillId()))throw new Exception("Ambiguous username fields.");
                t.user=n.getAutofillId();t.userDomain=domain;t.userScheme=scheme;
                t.explicitUsername|=explicitUsername;
            }
        }
        for(int i=0;i<n.getChildCount();i++)scan(n.getChildAt(i),t,domain,scheme,depth+1);
    }
    @Override public void onSaveRequest(SaveRequest request,SaveCallback callback){callback.onSuccess();}
}
