package com.bosha.vault;

import android.app.*;
import android.content.*;
import android.os.*;
import android.service.autofill.Dataset;
import android.view.autofill.*;
import android.widget.*;
import java.util.*;
import java.util.concurrent.*;
import org.json.*;

public final class FillActivity extends Activity {
    private VaultEngine engine;private FillTarget target;
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private boolean stopped,busy;private int attempts;private long retryAfter;
    @Override public void onCreate(Bundle state){super.onCreate(state);setResult(RESULT_CANCELED);
        try{String nonce=getIntent().getStringExtra("request");target=nonce==null?null:VaultAutofillService.REQUESTS.remove(nonce);if(target==null)throw new Exception("Autofill request expired.");target.validate(this);engine=Store.engine(this);if(!Store.vault(this).exists())throw new Exception("Create or import your vault in BoshaVault first.");showUnlock();}
        catch(Exception e){Ui.error(this,e);finish();}
    }
    private void showUnlock(){LinearLayout root=Ui.screen(this);Ui.gap(root,20);root.addView(Ui.text(this,"BoshaVault",28,Ui.INK,true));Ui.gap(root,20);root.addView(Ui.text(this,"Unlock to fill",22,Ui.INK,true));Ui.gap(root,10);root.addView(Ui.text(this,"Destination: "+(target.browser?target.domain:target.pkg),13,Ui.MUTED,false));Ui.gap(root,22);EditText p=Ui.field(root,"Master passphrase","",true,1024);
        root.addView(Ui.button(this,"Unlock & choose a login",true,()->{if(SystemClock.elapsedRealtime()<retryAfter){Toast.makeText(this,"Wait before trying again.",Toast.LENGTH_SHORT).show();return;}String password=p.getText().toString();p.setText("");run(()->engine.open(password),this::afterUnlock);}));
        if(Biometrics.available(this))root.addView(Ui.button(this,"Use fingerprint",false,()->Biometrics.unlock(this,(key,error)->{if(error!=null){Ui.error(this,error);return;}run(()->{try{engine.openWithKey(key);}finally{Arrays.fill(key,(byte)0);}},this::afterUnlock);} )));
        root.addView(Ui.button(this,"Cancel",false,this::finish));
    }
    interface Work{void work()throws Exception;}
    private void run(Work work,Runnable done){if(busy||stopped)return;busy=true;worker.execute(()->{Exception failure=null;try{work.work();}catch(Exception e){failure=e;}Exception err=failure;runOnUiThread(()->{busy=false;if(stopped)return;if(err!=null){attempts++;retryAfter=SystemClock.elapsedRealtime()+Math.min(30000,1000L<<Math.min(attempts,5));Ui.error(this,err);}else done.run();});});}
    private void afterUnlock(){try{target.validate(this);if(target.browser&&!engine.trusted(target.pkg,target.certificate,true)){new AlertDialog.Builder(this).setTitle("Approve this browser identity?").setMessage("Only approve a browser you installed from a trusted source.\n\n"+target.pkg+"\nCertificate SHA-256:\n"+target.certificate+"\n\nWebsite: "+target.domain+"\nThe first approval records this certificate; later requests must match it.").setNegativeButton("Cancel",(d,w)->finish()).setPositiveButton("Approve",(d,w)->run(()->engine.trust(target.pkg,target.certificate,true,null),()->choose(false))).show();}else choose(false);}catch(Exception e){Ui.error(this,e);finish();}}
    private void choose(boolean link){
        try{target.validate(this);LinearLayout root=Ui.screen(this);root.addView(Ui.text(this,"Choose a login",25,Ui.INK,true));Ui.gap(root,10);root.addView(Ui.text(this,target.browser?"Exact website: "+target.domain:"App: "+target.pkg,13,Ui.MUTED,false));Ui.gap(root,18);JSONArray entries=engine.snapshot().getJSONArray("entries");int count=0;
            for(int i=0;i<entries.length();i++){JSONObject e=entries.getJSONObject(i);if(e.getBoolean("deleted")||(target.password!=null&&e.getString("password").isEmpty())||e.getString("username").isEmpty())continue;
                if(target.browser){if(e.getString("url").isEmpty()||!VaultEngine.host(e.getString("url")).equals(VaultEngine.host("https://"+target.domain)))continue;}
                else if(!link&&!engine.linked(target.pkg,target.certificate,e.getString("id")))continue;
                count++;LinearLayout card=Ui.card(this,android.graphics.Color.WHITE);card.addView(Ui.text(this,e.getString("title"),16,Ui.INK,true));Ui.gap(card,7);card.addView(Ui.text(this,e.getString("username"),13,Ui.MUTED,false));card.setOnClickListener(v->{if(!target.browser&&link)confirmLink(e);else deliver(e);});root.addView(card);
            }
            if(count==0)root.addView(Ui.text(this,"No matching logins. Add one in the main app.",14,Ui.MUTED,false));
            if(!target.browser)root.addView(Ui.button(this,"Link a login to this app",false,()->choose(true)));
            root.addView(Ui.button(this,"Cancel",false,this::finish));
        }catch(Exception e){Ui.error(this,e);finish();}
    }
    private void confirmLink(JSONObject e){new AlertDialog.Builder(this).setTitle("Share this login with this app?").setMessage(e.optString("title")+"\n\nApp: "+target.pkg+"\nCertificate SHA-256:\n"+target.certificate+"\n\nOnly approve the official app you intended to use. This links only the selected login to that app identity.").setNegativeButton("Cancel",null).setPositiveButton("Link & fill",(d,w)->run(()->engine.trust(target.pkg,target.certificate,false,e.getString("id")),()->deliver(e))).show();}
    private void deliver(JSONObject entry){
        try{target.validate(this);RemoteViews label=new RemoteViews(getPackageName(),android.R.layout.simple_list_item_1);label.setTextViewText(android.R.id.text1,entry.getString("username"));Dataset.Builder dataset=new Dataset.Builder(label);if(target.user!=null)dataset.setValue(target.user,AutofillValue.forText(entry.getString("username")));if(target.password!=null)dataset.setValue(target.password,AutofillValue.forText(entry.getString("password")));Intent result=new Intent().putExtra(AutofillManager.EXTRA_AUTHENTICATION_RESULT,dataset.build());setResult(RESULT_OK,result);engine.close();finish();}
        catch(Exception e){Ui.error(this,e);finish();}
    }
    @Override protected void onStop(){super.onStop();stopped=true;if(engine!=null)worker.execute(engine::close);finish();}
    @Override protected void onDestroy(){worker.shutdown();super.onDestroy();}
}
