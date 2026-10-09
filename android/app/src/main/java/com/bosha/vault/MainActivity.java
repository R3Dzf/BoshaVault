package com.bosha.vault;

import android.app.*;
import android.content.*;
import android.content.ClipboardManager;
import android.net.Uri;
import android.os.*;
import android.provider.Settings;
import android.text.*;
import android.text.InputType;
import android.view.*;
import android.widget.*;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;
import org.json.*;

public final class MainActivity extends Activity {
    private VaultEngine engine;
    private LinearLayout root,list;
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private final Handler handler=new Handler(Looper.getMainLooper());
    private volatile int epoch; private int timeout=2,attempts; private volatile boolean active; private boolean busy; private long lastTouch,retryAfter,clipboardUntil;
    private String filter="all",search="",clipboard;
    private byte[] exportBytes,pendingImport; private LocalTransfer pendingTransfer;
    private String scannedPairing;
    private final List<AlertDialog> dialogs=new ArrayList<>();
    private final Runnable tick=new Runnable(){public void run(){if(active&&engine!=null&&engine.isOpen()&&SystemClock.elapsedRealtime()-lastTouch>timeout*60000L)lock();handler.postDelayed(this,1000);}};
    @Override public void onCreate(Bundle b){super.onCreate(b);try{engine=Store.engine(this);}catch(Exception e){Ui.error(this,e);finish();return;}handler.post(tick);}
    @Override protected void onStart(){super.onStart();active=true;showLock();}
    @Override protected void onResume(){super.onResume();if(scannedPairing!=null){String code=scannedPairing;scannedPairing=null;showPairing(Store.vault(this).exists(),code);}}
    @Override protected void onStop(){super.onStop();active=false;epoch++;for(AlertDialog d:new ArrayList<>(dialogs))d.dismiss();dialogs.clear();if(root!=null)root.removeAllViews();worker.execute(()->engine.close());}
    @Override protected void onDestroy(){handler.removeCallbacks(tick);worker.shutdown();super.onDestroy();}
    @Override public boolean dispatchTouchEvent(android.view.MotionEvent e){lastTouch=SystemClock.elapsedRealtime();return super.dispatchTouchEvent(e);}
    @Override public void onUserInteraction(){super.onUserInteraction();lastTouch=SystemClock.elapsedRealtime();}
    @Override public void onBackPressed(){if(engine.isOpen()){lock();}else super.onBackPressed();}
    interface Work{void run()throws Exception;}
    private void task(Work action,Runnable done){
        if(busy)return;busy=true;int stamp=epoch;
        worker.execute(()->{Exception failure=null;try{action.run();}catch(Exception e){failure=e;}if(!active||stamp!=epoch)engine.close();Exception error=failure;runOnUiThread(()->{busy=false;if(!active||stamp!=epoch)return;if(error!=null)Ui.error(this,error);else done.run();});});
    }
    private void lock(){epoch++;for(AlertDialog d:new ArrayList<>(dialogs))d.dismiss();dialogs.clear();clearClipboard();worker.execute(()->engine.close());showLock();}
    private void toast(String s){Toast.makeText(this,s,Toast.LENGTH_LONG).show();}
    private void showLock(){
        if(!active)return;root=Ui.screen(this);Ui.gap(root,35);LinearLayout logo=Ui.card(this,Ui.SOFT);logo.addView(Ui.text(this,"B",48,Ui.PURPLE,true));logo.addView(Ui.text(this,"Your private space.",27,Ui.INK,true));Ui.gap(logo,9);logo.addView(Ui.text(this,"Encrypted. Local. Yours.",14,Ui.MUTED,false));root.addView(logo);Ui.gap(root,25);
        boolean exists=Store.vault(this).exists();root.addView(Ui.text(this,exists?"Welcome back":"Make yourself at home",25,Ui.INK,true));Ui.gap(root,9);root.addView(Ui.text(this,exists?"Unlock with your master passphrase.":"Choose a unique passphrase with at least 16 characters. A long phrase is easier to remember.",13,Ui.MUTED,false));Ui.gap(root,25);
        EditText master=Ui.field(root,"Master passphrase","",true,1024),confirm=exists?null:Ui.field(root,"Confirm passphrase","",true,1024);
        root.addView(Ui.button(this,exists?"Unlock vault →":"Create encrypted vault →",true,()->{
            if(SystemClock.elapsedRealtime()<retryAfter){toast("Please wait before trying another passphrase.");return;}String password=master.getText().toString();if(confirm!=null&&!password.equals(confirm.getText().toString())){toast("Passphrases do not match.");return;}master.setText("");if(confirm!=null)confirm.setText("");
            task(()->{try{if(exists)engine.open(password);else engine.create(password);attempts=0;}catch(Exception error){attempts++;retryAfter=SystemClock.elapsedRealtime()+Math.min(30000,1000L<<Math.min(attempts,5));throw error;}},this::showVault);
        }));
        if(exists&&Biometrics.available(this))root.addView(Ui.button(this,"Unlock with fingerprint",false,()->Biometrics.unlock(this,(key,error)->{if(error!=null){Ui.error(this,error);return;}task(()->{try{engine.openWithKey(key);}finally{Arrays.fill(key,(byte)0);}},this::showVault);} )));
        root.addView(Ui.button(this,"Import encrypted vault",false,this::chooseImport));
        if(!exists)root.addView(Ui.button(this,"Receive from your Windows app",false,()->showPairing(false)));
        Ui.gap(root,25);root.addView(Ui.text(this,"No account. No subscription. No extension.\nPreview release · independent review pending.\nA lost passphrase cannot be reset.",11,Ui.MUTED,false));
        if(pendingImport!=null)handler.post(()->importPrompt(pendingImport));
    }
    private void showVault(){
        if(!engine.isOpen()||!active)return;lastTouch=SystemClock.elapsedRealtime();root=Ui.screen(this);
        LinearLayout top=new LinearLayout(this);top.setGravity(Gravity.CENTER_VERTICAL);top.addView(Ui.text(this,"BoshaVault",23,Ui.INK,true),new LinearLayout.LayoutParams(0,-2,1));TextView lock=Ui.text(this,"Lock",13,Ui.PURPLE,true);lock.setPadding(15,12,15,12);lock.setOnClickListener(v->lock());top.addView(lock);root.addView(top);Ui.gap(root,25);
        root.addView(Ui.text(this,"A little peace of mind.",13,Ui.MUTED,false));Ui.gap(root,6);root.addView(Ui.text(this,filter.equals("trash")?"Your trash":filter.equals("favorites")?"Your favorites":filter.equals("health")?"Passwords to review":"Your vault",30,Ui.INK,true));Ui.gap(root,20);
        try{
            JSONArray entries=engine.snapshot().getJSONArray("entries");int count=0;for(int i=0;i<entries.length();i++)if(!entries.getJSONObject(i).getBoolean("deleted"))count++;
            LinearLayout stats=Ui.card(this,Ui.SOFT);stats.addView(Ui.text(this,"SAVED LOGINS",10,Ui.PURPLE,true));stats.addView(Ui.text(this,Integer.toString(count),30,Ui.INK,true));stats.addView(Ui.text(this,"Encrypted on this device · offline first",12,Ui.MUTED,false));root.addView(stats);
        }catch(Exception e){Ui.error(this,e);}
        EditText find=Ui.field(root,"Search your vault",search,false,200);find.setHint("Name, username or website");find.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int a,int c,int f){}public void onTextChanged(CharSequence s,int a,int b,int c){search=s.toString();renderList();}public void afterTextChanged(Editable e){}});
        LinearLayout tabs=new LinearLayout(this);String[] values={"all","favorites","health","trash"},labels={"All","Favorites","Review","Trash"};for(int i=0;i<4;i++){final String value=values[i];TextView t=Ui.text(this,labels[i],12,filter.equals(value)?Ui.PURPLE:Ui.MUTED,true);t.setPadding(Ui.dp(this,9),Ui.dp(this,12),Ui.dp(this,9),Ui.dp(this,12));t.setOnClickListener(v->{filter=value;showVault();});tabs.addView(t,new LinearLayout.LayoutParams(0,-2,1));}root.addView(tabs);
        list=Ui.column(this);root.addView(list);renderList();root.addView(Ui.button(this,"+  Add login",true,()->edit(null)));Ui.gap(root,15);
        root.addView(Ui.button(this,"Password generator",false,this::generator));root.addView(Ui.button(this,"Devices & encrypted backup",false,this::devices));root.addView(Ui.button(this,"Settings & Autofill",false,this::settings));Ui.gap(root,15);root.addView(Ui.text(this,"● Local & encrypted  ·  Locks when you leave the app",11,Ui.MUTED,false));
    }
    private void renderList(){
        if(list==null||!engine.isOpen())return;list.removeAllViews();
        try{
            JSONArray entries=engine.snapshot().getJSONArray("entries");List<JSONObject> live=new ArrayList<>();Map<String,Integer> passwords=new HashMap<>();
            for(int i=0;i<entries.length();i++){JSONObject e=entries.getJSONObject(i);if(!e.getBoolean("deleted"))passwords.put(e.getString("password"),passwords.getOrDefault(e.getString("password"),0)+1);}
            for(int i=0;i<entries.length();i++){
                JSONObject e=entries.getJSONObject(i);if(e.getBoolean("deleted")!=filter.equals("trash"))continue;if(filter.equals("favorites")&&!e.getBoolean("favorite"))continue;
                if(filter.equals("health")&&e.getString("password").length()>=16&&passwords.getOrDefault(e.getString("password"),0)<2)continue;
                String haystack=(e.getString("title")+" "+e.getString("username")+" "+e.getString("url")+" "+e.getString("folder")).toLowerCase(Locale.ROOT);if(!haystack.contains(search.toLowerCase(Locale.ROOT)))continue;live.add(e);
            }
            live.sort(Comparator.comparing(e->e.optString("title").toLowerCase(Locale.ROOT)));
            for(JSONObject e:live){LinearLayout card=Ui.card(this,android.graphics.Color.WHITE);TextView name=Ui.text(this,e.getString("title")+(e.getBoolean("favorite")?"   ★":""),16,Ui.INK,true);card.addView(name);Ui.gap(card,6);card.addView(Ui.text(this,e.getString("username"),12,Ui.MUTED,false));card.setOnClickListener(v->detail(e));list.addView(card);}
            if(live.isEmpty()){LinearLayout c=Ui.card(this,Ui.GREEN);c.addView(Ui.text(this,"✧  Your private space awaits",16,Ui.INK,true));Ui.gap(c,7);c.addView(Ui.text(this,"Add a login or import an encrypted vault.",12,Ui.MUTED,false));list.addView(c);}
        }catch(Exception e){Ui.error(this,e);}
    }
    private AlertDialog dialog(String title,LinearLayout body){ScrollView scroll=new ScrollView(this);int p=Ui.dp(this,22);body.setPadding(p,p,p,p);scroll.addView(body);AlertDialog d=new AlertDialog.Builder(this).setTitle(title).setView(scroll).setNegativeButton("Close",null).create();dialogs.add(d);d.setOnDismissListener(x->dialogs.remove(d));d.show();d.getWindow().setFlags(WindowManager.LayoutParams.FLAG_SECURE,WindowManager.LayoutParams.FLAG_SECURE);return d;}
    private void detail(JSONObject entry){
        try{
            LinearLayout b=Ui.column(this);b.addView(Ui.text(this,entry.getString("url").isEmpty()?"No website saved":VaultEngine.host(entry.getString("url")),12,Ui.MUTED,false));Ui.gap(b,16);b.addView(Ui.text(this,"Username / email",12,Ui.MUTED,false));b.addView(Ui.text(this,entry.getString("username"),16,Ui.INK,false));b.addView(Ui.button(this,"Copy username",false,()->copy(entry.optString("username"))));Ui.gap(b,15);TextView password=Ui.text(this,"••••••••••••••••",20,Ui.INK,false);b.addView(password);b.addView(Ui.button(this,"Show for 15 seconds",false,()->{password.setText(entry.optString("password"));handler.postDelayed(()->password.setText("••••••••••••••••"),15000);}));b.addView(Ui.button(this,"Copy password",true,()->copy(entry.optString("password"))));Ui.gap(b,15);b.addView(Ui.text(this,entry.getString("notes"),13,Ui.INK,false));AlertDialog d=dialog(entry.getString("title"),b);
            b.addView(Ui.button(this,"Edit login",false,()->{d.dismiss();edit(entry);}));
            if(!entry.getString("url").isEmpty())b.addView(Ui.button(this,"Open website",false,()->startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse(entry.optString("url"))))));
            boolean deleted=entry.getBoolean("deleted");b.addView(Ui.button(this,deleted?"Restore login":"Move to trash",false,()->task(()->{JSONObject copy=new JSONObject(entry.toString());copy.put("deleted",!deleted);engine.upsert(copy);},()->{d.dismiss();showVault();})));
        }catch(Exception e){Ui.error(this,e);}
    }
    private void edit(JSONObject original){
        try{
            JSONObject entry=original==null?VaultEngine.blank():new JSONObject(original.toString());LinearLayout b=Ui.column(this);EditText title=Ui.field(b,"Name",entry.getString("title"),false,200),username=Ui.field(b,"Username / email",entry.getString("username"),false,2000),password=Ui.field(b,"Password",entry.getString("password"),true,4096);
            b.addView(Ui.button(this,"Generate a strong password",false,()->{try{password.setText(VaultEngine.generate(24,true));}catch(Exception e){Ui.error(this,e);}}));Ui.gap(b,15);EditText url=Ui.field(b,"Website (https://example.com)",entry.getString("url"),false,2048),folder=Ui.field(b,"Folder",entry.getString("folder"),false,100),notes=Ui.field(b,"Private notes",entry.getString("notes"),false,32000);notes.setSingleLine(false);notes.setMinLines(2);CheckBox favorite=new CheckBox(this);favorite.setText("Add to favorites");favorite.setChecked(entry.getBoolean("favorite"));b.addView(favorite);AlertDialog d=dialog(original==null?"Add a new login":"Edit login",b);
            b.addView(Ui.button(this,"Save login",true,()->{
                try{if(title.getText().toString().trim().isEmpty())throw new Exception("Give this login a name.");String website=url.getText().toString().trim();if(!website.isEmpty())VaultEngine.host(website);entry.put("title",title.getText().toString().trim()).put("username",username.getText().toString()).put("password",password.getText().toString()).put("url",website).put("folder",folder.getText().toString()).put("notes",notes.getText().toString()).put("favorite",favorite.isChecked()).put("deleted",false);task(()->engine.upsert(entry),()->{password.setText("");username.setText("");notes.setText("");d.dismiss();showVault();});}catch(Exception e){Ui.error(this,e);}
            }));
        }catch(Exception e){Ui.error(this,e);}
    }
    private void generator(){
        LinearLayout b=Ui.column(this);EditText value=Ui.field(b,"Generated on this device","",false,128);value.setFocusable(false);try{value.setText(VaultEngine.generate(24,true));}catch(Exception e){Ui.error(this,e);}TextView label=Ui.text(this,"Length: 24",13,Ui.MUTED,false);b.addView(label);SeekBar length=new SeekBar(this);length.setMax(48);length.setProgress(8);b.addView(length);length.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){public void onProgressChanged(SeekBar s,int p,boolean u){label.setText("Length: "+(p+16));}public void onStartTrackingTouch(SeekBar s){}public void onStopTrackingTouch(SeekBar s){}});CheckBox symbols=new CheckBox(this);symbols.setText("Include symbols");symbols.setChecked(true);b.addView(symbols);b.addView(Ui.button(this,"Generate again",false,()->{try{value.setText(VaultEngine.generate(length.getProgress()+16,symbols.isChecked()));}catch(Exception e){Ui.error(this,e);}}));b.addView(Ui.button(this,"Copy password",true,()->copy(value.getText().toString())));dialog("Make a stronger password",b);
    }
    private void copy(String value){ClipboardManager c=(ClipboardManager)getSystemService(CLIPBOARD_SERVICE);ClipData data=ClipData.newPlainText("BoshaVault",value);if(Build.VERSION.SDK_INT>=33){PersistableBundle extras=new PersistableBundle();extras.putBoolean(ClipDescription.EXTRA_IS_SENSITIVE,true);data.getDescription().setExtras(extras);}c.setPrimaryClip(data);clipboard=value;clipboardUntil=SystemClock.elapsedRealtime()+20000;handler.postDelayed(()->{if(SystemClock.elapsedRealtime()>=clipboardUntil)clearClipboard();},20000);toast("Copied. Clipboard clears in 20 seconds. Paste into the intended app.");}
    private void clearClipboard(){try{ClipboardManager c=(ClipboardManager)getSystemService(CLIPBOARD_SERVICE);ClipData d=c.getPrimaryClip();if(clipboard!=null&&d!=null&&d.getItemCount()>0&&clipboard.contentEquals(d.getItemAt(0).coerceToText(this)))c.clearPrimaryClip();clipboard=null;}catch(Exception ignored){}}
    private void devices(){LinearLayout b=Ui.column(this);b.addView(Ui.text(this,"Transfer encrypted data only. Both devices stay usable offline. Keep a backup away from this phone.",13,Ui.MUTED,false));b.addView(Ui.button(this,"Export encrypted backup",true,()->{try{exportBytes=engine.export();startActivityForResult(new Intent(Intent.ACTION_CREATE_DOCUMENT).setType("application/octet-stream").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_TITLE,"BoshaVault.boshavault"),11);}catch(Exception e){Ui.error(this,e);}}));b.addView(Ui.button(this,"Import / merge encrypted backup",false,this::chooseImport));b.addView(Ui.button(this,"Connect to Windows over Wi-Fi",false,()->showPairing(true)));dialog("Devices & encrypted backup",b);}
    private void chooseImport(){startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE),12);}
    @Override protected void onActivityResult(int request,int result,Intent intent){super.onActivityResult(request,result,intent);if(request==13){scannedPairing="";if(result==RESULT_OK&&intent!=null){String code=intent.getStringExtra(ScanQrActivity.RESULT_PAIRING);try{new LocalTransfer(code);scannedPairing=code;}catch(Exception e){toast("The scanned code is not a valid BoshaVault pairing code.");}}return;}if(result!=RESULT_OK||intent==null||intent.getData()==null){exportBytes=null;return;}Uri uri=intent.getData();
        if(request==11&&exportBytes!=null){byte[] bytes=exportBytes;exportBytes=null;task(()->{try(OutputStream out=getContentResolver().openOutputStream(uri,"wt")){if(out==null)throw new Exception("Cannot write the backup.");out.write(bytes);}},()->toast("Encrypted backup saved."));}
        else if(request==12){task(()->{try(InputStream in=getContentResolver().openInputStream(uri)){if(in==null)throw new Exception("Cannot read this file.");pendingImport=VaultEngine.bounded(in);}},()->importPrompt(pendingImport));}
    }
    private void importPrompt(byte[] bytes){
        if(bytes==null)return;pendingImport=null;
        boolean exists=Store.vault(this).exists(),open=engine.isOpen();
        if(open){try{if(!engine.needsPassphrase(bytes)){task(()->engine.merge(bytes),()->{toast("Backup merged. Concurrent edits are kept as conflict copies.");showVault();});return;}}catch(Exception e){Ui.error(this,e);return;}}
        LinearLayout b=Ui.column(this);EditText password=Ui.field(b,exists?"Current vault passphrase":"Backup master passphrase","",true,1024);EditText incoming=exists?Ui.field(b,"Incoming backup passphrase (if key was rotated)","",true,1024):null;b.addView(Ui.text(this,exists?"Unlock the current vault to merge. A newer key is adopted after verifying its passphrase; local edits are retained.":"Import this encrypted vault to start using it on this phone.",13,Ui.MUTED,false));AlertDialog d=dialog("Import encrypted vault",b);
        b.addView(Ui.button(this,"Import / merge",true,()->{String p=password.getText().toString(),r=incoming==null?"":incoming.getText().toString();password.setText("");if(incoming!=null)incoming.setText("");task(()->{if(exists){if(!engine.isOpen())engine.open(p);if(engine.needsPassphrase(bytes)){engine.mergeUsingPassword(bytes,r.isEmpty()?p:r);Biometrics.disable(this);}else engine.merge(bytes);}else{VaultEngine.importNew(Store.vault(this),bytes,p,Store.device(this));engine.open(p);engine.revokeTrust();}},()->{d.dismiss();showVault();});}));
    }
    private void showPairing(boolean existing){
        showPairing(existing,"");
    }
    private void showPairing(boolean existing,String scanned){
        LinearLayout b=Ui.column(this);b.addView(Ui.text(this,"Start a private transfer in the Windows app. Scan its QR or paste the temporary pairing code. Use the same private Wi-Fi network.",13,Ui.MUTED,false));
        b.addView(Ui.button(this,"Scan QR code",true,()->{if(!busy)startActivityForResult(new Intent(this,ScanQrActivity.class),13);}));Ui.gap(b,18);
        EditText code=Ui.field(b,"Pairing code (BV1:…)",scanned,false,4096);code.setSingleLine(false);code.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO);code.setSelectAllOnFocus(true);
        if(!scanned.isEmpty())b.addView(Ui.text(this,"QR scanned. Tap Connect & transfer to continue.",13,Ui.PURPLE,true));
        AlertDialog d=dialog("Connect your devices",b);d.setOnDismissListener(x->{code.setText("");dialogs.remove(d);});
        b.addView(Ui.button(this,"Connect & transfer",true,()->{String c=code.getText().toString().trim();code.setText("");task(()->{pendingTransfer=new LocalTransfer(c);pendingImport=pendingTransfer.download();if(engine.isOpen()&&!engine.needsPassphrase(pendingImport)){engine.merge(pendingImport);pendingTransfer.upload(engine.export());pendingImport=null;pendingTransfer=null;}},()->{d.dismiss();if(pendingImport==null){toast("Both devices have the merged vault.");showVault();}else pairingImportPrompt(pendingImport);});}));
    }
    private void pairingImportPrompt(byte[] bytes){
        pendingImport=null;boolean exists=Store.vault(this).exists();LinearLayout b=Ui.column(this);EditText master=Ui.field(b,exists?"Current phone passphrase":"Windows vault passphrase","",true,1024);EditText incoming=exists?Ui.field(b,"Incoming passphrase if rotated","",true,1024):null;b.addView(Ui.text(this,"Unlock the received vault locally. A newer key is adopted only after verification. No passphrase is sent over the network.",13,Ui.MUTED,false));AlertDialog d=dialog("Complete the encrypted transfer",b);
        b.addView(Ui.button(this,"Unlock & complete transfer",true,()->{String p=master.getText().toString(),r=incoming==null?"":incoming.getText().toString();master.setText("");if(incoming!=null)incoming.setText("");task(()->{if(exists){if(!engine.isOpen())engine.open(p);if(engine.needsPassphrase(bytes)){engine.mergeUsingPassword(bytes,r.isEmpty()?p:r);Biometrics.disable(this);}else engine.merge(bytes);}else{VaultEngine.importNew(Store.vault(this),bytes,p,Store.device(this));engine.open(p);engine.revokeTrust();}pendingTransfer.upload(engine.export());pendingTransfer=null;},()->{d.dismiss();toast("Transfer complete.");showVault();});}));
    }
    private void settings(){
        LinearLayout b=Ui.column(this);b.addView(Ui.text(this,"Autofill uses Android's built-in framework. The vault is unlocked for each fill request. Only exact HTTPS hosts or apps you explicitly link are eligible.",13,Ui.MUTED,false));b.addView(Ui.button(this,"Enable Android Autofill",true,()->{try{startActivity(new Intent(Settings.ACTION_REQUEST_SET_AUTOFILL_SERVICE,Uri.parse("package:"+getPackageName())));}catch(Exception e){startActivity(new Intent(Settings.ACTION_SETTINGS));}}));
        b.addView(Ui.button(this,Biometrics.available(this)?"Disable fingerprint unlock":"Enable fingerprint unlock",false,()->{
            try{if(Biometrics.available(this)){Biometrics.disable(this);toast("Fingerprint unlock disabled.");}else Biometrics.enable(this,engine.biometricKey(),(key,error)->{if(error!=null)Ui.error(this,error);else toast("Fingerprint unlock enabled for this phone.");});}catch(Exception e){Ui.error(this,e);}
        }));b.addView(Ui.button(this,"Revoke all Autofill app approvals",false,()->task(engine::revokeTrust,()->toast("Approvals revoked. Apps must be approved again."))));
        Ui.gap(b,20);EditText current=Ui.field(b,"Current master passphrase","",true,1024),next=Ui.field(b,"New master passphrase (16+ characters)","",true,1024),confirm=Ui.field(b,"Confirm new passphrase","",true,1024);b.addView(Ui.button(this,"Change master passphrase",false,()->{String c=current.getText().toString(),n=next.getText().toString();if(!n.equals(confirm.getText().toString())){toast("Passphrases do not match.");return;}current.setText("");next.setText("");confirm.setText("");task(()->{engine.changePassword(c,n);Biometrics.disable(this);},()->toast("Passphrase changed. Old backups still use their original passphrase."));}));Ui.gap(b,16);b.addView(Ui.text(this,"Screenshots are blocked where Android supports it. Copying a password exposes it to the clipboard. Malware on an unlocked device can still steal data. No cloud sync, passkey storage or breach lookup is included.",11,Ui.MUTED,false));dialog("Your vault settings",b);
    }
}
