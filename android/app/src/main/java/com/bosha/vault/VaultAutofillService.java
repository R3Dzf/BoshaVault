package com.bosha.vault;

import android.app.*;
import android.app.assist.AssistStructure;
import android.content.*;
import android.os.*;
import android.service.autofill.*;
import android.text.InputType;
import android.view.View;
import android.view.autofill.*;
import android.widget.RemoteViews;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;

public final class VaultAutofillService extends AutofillService {
    static final Map<String,FillTarget> REQUESTS=new ConcurrentHashMap<>();
    private int nodes;
    @Override public void onFillRequest(FillRequest request,CancellationSignal cancel,FillCallback callback){
        try{
            if(cancel.isCanceled()||request.getFillContexts().isEmpty()){callback.onSuccess(null);return;}
            AssistStructure structure=request.getFillContexts().get(request.getFillContexts().size()-1).getStructure();FillTarget target=new FillTarget();
            if(structure.getActivityComponent()==null){callback.onSuccess(null);return;}target.pkg=structure.getActivityComponent().getPackageName();if(target.pkg.equals(getPackageName())){callback.onSuccess(null);return;}
            target.certificate=FillTarget.certificate(this,target.pkg);target.browser=FillTarget.BROWSERS.contains(target.pkg);nodes=0;
            for(int i=0;i<structure.getWindowNodeCount();i++)scan(structure.getWindowNodeAt(i).getRootViewNode(),target,null,null,0);
            if(target.password==null){callback.onSuccess(null);return;}target.validate(this);
            REQUESTS.entrySet().removeIf(x->SystemClock.elapsedRealtime()-x.getValue().created>180000);if(REQUESTS.size()>=8){callback.onSuccess(null);return;}
            String nonce=UUID.randomUUID().toString();REQUESTS.put(nonce,target);Intent i=new Intent(this,FillActivity.class).putExtra("request",nonce);
            PendingIntent intent=PendingIntent.getActivity(this,nonce.hashCode(),i,PendingIntent.FLAG_CANCEL_CURRENT|PendingIntent.FLAG_IMMUTABLE);
            List<AutofillId> ids=new ArrayList<>();if(target.user!=null)ids.add(target.user);ids.add(target.password);
            RemoteViews label=new RemoteViews(getPackageName(),android.R.layout.simple_list_item_1);label.setTextViewText(android.R.id.text1,"Unlock BoshaVault · "+(target.browser?target.domain:target.pkg));
            if(cancel.isCanceled()){REQUESTS.remove(nonce);callback.onSuccess(null);return;}
            callback.onSuccess(new FillResponse.Builder().setAuthentication(ids.toArray(new AutofillId[0]),intent.getIntentSender(),label).build());
        }catch(Exception e){callback.onSuccess(null);}
    }
    private void scan(AssistStructure.ViewNode n,FillTarget t,String domain,String scheme,int depth)throws Exception{
        if(++nodes>2000||depth>32)throw new Exception("Form too large.");
        if(n.getWebDomain()!=null){domain=n.getWebDomain();scheme=n.getWebScheme();}
        int type=n.getInputType(),variation=type&InputType.TYPE_MASK_VARIATION;
        boolean password=(type&InputType.TYPE_MASK_CLASS)==InputType.TYPE_CLASS_TEXT&&(variation==InputType.TYPE_TEXT_VARIATION_PASSWORD||variation==InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD||variation==InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD);
        boolean user=(type&InputType.TYPE_MASK_CLASS)==InputType.TYPE_CLASS_TEXT&&(variation==InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS||variation==InputType.TYPE_TEXT_VARIATION_WEB_EMAIL_ADDRESS);
        String[] hints=n.getAutofillHints();if(hints!=null)for(String h:hints){password|=h.equals(View.AUTOFILL_HINT_PASSWORD);user|=h.equals(View.AUTOFILL_HINT_USERNAME)||h.equals(View.AUTOFILL_HINT_EMAIL_ADDRESS);}
        if(n.getVisibility()==View.VISIBLE&&n.getAutofillId()!=null){
            if(password){if(t.password!=null&&!t.password.equals(n.getAutofillId()))throw new Exception("Ambiguous password fields.");t.password=n.getAutofillId();t.domain=domain;t.scheme=scheme;}
            if(user){if(t.user!=null&&!t.user.equals(n.getAutofillId()))throw new Exception("Ambiguous username fields.");t.user=n.getAutofillId();t.userDomain=domain;t.userScheme=scheme;}
        }
        for(int i=0;i<n.getChildCount();i++)scan(n.getChildAt(i),t,domain,scheme,depth+1);
    }
    @Override public void onSaveRequest(SaveRequest request,SaveCallback callback){callback.onSuccess();} // Saving is an explicit action in the app.
}
