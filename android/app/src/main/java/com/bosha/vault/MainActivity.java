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
import java.util.function.Consumer;
import org.json.*;

public final class MainActivity extends Activity {
    private VaultEngine engine;
    private LinearLayout root;
    private ListView list;
    private BaseAdapter entryAdapter;
    private TextView vaultStats;
    private final List<JSONObject> displayedEntries=new ArrayList<>();
    private String folderFilter;

    private final ExecutorService worker=Executors.newSingleThreadExecutor(),cloudWorker=Executors.newSingleThreadExecutor();
    private final Handler handler=new Handler(Looper.getMainLooper());
    private volatile int epoch; private int timeout=2,attempts; private volatile boolean active; private boolean busy; private long lastTouch,retryAfter,clipboardUntil;
    private String filter="all",search="",clipboard,undoEntryId;private long undoUntil;private volatile boolean cloudOpen;private volatile long nextCloudCheck;private long autoSessionUntil;private TextView syncStatus;private volatile CloudClient autoClient;private volatile CloudAuth.Session autoSession;private String autoSessionSlot;
    private byte[] exportBytes,pendingImport; private volatile LocalTransfer pendingTransfer;
    private String scannedPairing,scannedCloud,scannedSecure;
    private final List<AlertDialog> dialogs=new ArrayList<>();
    private final Runnable tick=new Runnable(){public void run(){if(active&&engine!=null&&engine.isOpen()&&SystemClock.elapsedRealtime()-lastTouch>timeout*60000L)lock();if(active&&engine!=null&&engine.isOpen())autoSyncTick();handler.postDelayed(this,1000);}};
    @Override public void onCreate(Bundle b){super.onCreate(b);try{engine=Store.engine(this);CloudSyncJob.schedule(this);}catch(Exception e){Ui.error(this,e);finish();return;}handler.post(tick);}
    @Override protected void onStart(){super.onStart();active=true;showLock();}
    @Override protected void onResume(){super.onResume();if(scannedPairing!=null){String code=scannedPairing;scannedPairing=null;showPairing(Store.vault(this).exists(),code);}if(scannedCloud!=null){String code=scannedCloud;scannedCloud=null;joinCloud(code);}if(scannedSecure!=null&&(!Store.vault(this).exists()||engine.isOpen())){String code=scannedSecure;scannedSecure=null;connectToPc(code);}}
    @Override protected void onStop(){super.onStop();active=false;epoch++;autoSession=null;CloudClient running=autoClient;if(running!=null)running.close();cancelLocalTransfer();for(AlertDialog d:new ArrayList<>(dialogs))d.dismiss();dialogs.clear();displayedEntries.clear();list=null;entryAdapter=null;vaultStats=null;syncStatus=null;if(root!=null)root.removeAllViews();worker.execute(()->engine.close());}
    @Override protected void onDestroy(){handler.removeCallbacks(tick);worker.shutdown();cloudWorker.shutdown();super.onDestroy();}
    @Override public boolean dispatchTouchEvent(android.view.MotionEvent e){lastTouch=SystemClock.elapsedRealtime();return super.dispatchTouchEvent(e);}
    @Override public void onUserInteraction(){super.onUserInteraction();lastTouch=SystemClock.elapsedRealtime();}
    @Override public void onBackPressed(){if(engine.isOpen()){lock();}else super.onBackPressed();}
    interface Work{void run()throws Exception;}
    private void task(Work action,Runnable done){
        if(busy)return;busy=true;int stamp=epoch;
        worker.execute(()->{Exception failure=null;try{action.run();}catch(Exception e){failure=e;}if(!active||stamp!=epoch)engine.close();Exception error=failure;runOnUiThread(()->{busy=false;if(!active||stamp!=epoch)return;if(error!=null)Ui.error(this,error);else{nextCloudCheck=0;done.run();}});});
    }
    private void cancelLocalTransfer(){LocalTransfer current=pendingTransfer;pendingTransfer=null;if(current!=null)current.cancel();}
    private void lock(){epoch++;undoEntryId=null;CloudClient running=autoClient;if(running!=null)running.close();cancelLocalTransfer();for(AlertDialog d:new ArrayList<>(dialogs))d.dismiss();dialogs.clear();displayedEntries.clear();list=null;entryAdapter=null;vaultStats=null;syncStatus=null;clearClipboard();worker.execute(()->engine.close());showLock();}
    private void autoSyncTick(){
        long now=SystemClock.elapsedRealtime();if(cloudOpen||busy||now<nextCloudCheck)return;nextCloudCheck=now+12000;
        int stamp=epoch;cloudWorker.execute(()->{
            JSONObject profile=null;CloudClient client=null;String result="Cloud not connected · open Sync";
            try{
                synchronized(CloudClient.GATE){
                    String id=engine.snapshot().getString("vaultId");CloudSettingsStore store=new CloudSettingsStore(this);profile=store.load(id);
                    if(profile==null)return;
                    if(!profile.optBoolean("autoSync",true)){result="Cloud paused on this phone";return;}
                    result="Cloud synced · up to date";
                    client=new CloudClient(()->{if(!active||stamp!=epoch||cloudOpen||!engine.isOpen())throw new IOException("Cloud operation cancelled.");});autoClient=client;
                    CloudAuth.Session signed=autoSession;
                    if(signed==null||SystemClock.elapsedRealtime()>=autoSessionUntil||!signed.owner.equals(profile.getString("ownerId"))||!profile.getString("vaultSlot").equals(autoSessionSlot)){
                        signed=client.refresh(profile,store);
                        if(client.auth.factors(signed).requiresChallenge(signed)){autoSession=null;result="Cloud paused · verify 2FA in Sync";nextCloudCheck=SystemClock.elapsedRealtime()+60000;return;}
                        autoSession=signed;autoSessionSlot=profile.getString("vaultSlot");autoSessionUntil=SystemClock.elapsedRealtime()+45000;
                    }
                    int conflicts=client.syncAutomatic(engine,null,profile,store,signed);if(conflicts>0)result="Cloud synced · "+conflicts+" conflict copies";
                }
            }catch(Exception e){autoSession=null;result="Cloud offline · changes retry automatically";}
            finally{CloudSettingsStore.clear(profile);if(client!=null)client.close();autoClient=null;String status=result;runOnUiThread(()->{if(active&&stamp==epoch&&syncStatus!=null){syncStatus.setText(status);if(status.startsWith("Cloud synced"))renderList();}});}
        });
    }
    private void toast(String s){Toast.makeText(this,s,Toast.LENGTH_LONG).show();}
    private void showLock(){
        if(!active)return;root=Ui.screen(this);Ui.gap(root,24);LinearLayout logo=Ui.card(this,Ui.SOFT);logo.addView(Ui.brandIcon(this,64));logo.addView(Ui.text(this,"Your private space.",27,Ui.INK,true));Ui.gap(logo,9);logo.addView(Ui.text(this,"Encrypted. Local. Yours.",14,Ui.MUTED,false));root.addView(logo);Ui.gap(root,17);
        boolean exists=Store.vault(this).exists();root.addView(Ui.text(this,exists?"Welcome back":"Create your private vault",25,Ui.INK,true));Ui.gap(root,9);root.addView(Ui.text(this,exists?"Unlock with your master passphrase.":"Choose a unique passphrase with at least 16 characters. A long phrase is easier to remember.",13,Ui.MUTED,false));Ui.gap(root,25);
        EditText master=Ui.field(root,"Master passphrase","",true,1024),confirm=exists?null:Ui.field(root,"Confirm passphrase","",true,1024);
        root.addView(Ui.button(this,exists?"Unlock vault →":"Create encrypted vault →",true,()->{
            if(SystemClock.elapsedRealtime()<retryAfter){toast("Please wait before trying another passphrase.");return;}String password=master.getText().toString();if(confirm!=null&&!password.equals(confirm.getText().toString())){toast("Passphrases do not match.");return;}master.setText("");if(confirm!=null)confirm.setText("");
            task(()->{try{if(exists)engine.open(password);else engine.create(password);attempts=0;}catch(Exception error){attempts++;retryAfter=SystemClock.elapsedRealtime()+Math.min(30000,1000L<<Math.min(attempts,5));throw error;}},this::showVault);
        }));
        if(exists&&Biometrics.available(this))root.addView(Ui.button(this,"Unlock with fingerprint",false,()->Biometrics.unlock(this,(key,error)->{if(error!=null){Ui.error(this,error);return;}task(()->{try{engine.openWithKey(key);}finally{Arrays.fill(key,(byte)0);}},this::showVault);} )));
        root.addView(Ui.button(this,"Import encrypted vault",false,this::chooseImport));
        root.addView(Ui.button(this,"Check for app updates",false,this::appUpdates));
        if(!exists)root.addView(Ui.button(this,"Receive from your Windows app",false,()->showPairing(false)));
        if(!exists)root.addView(Ui.button(this,"Connect to my PC · secure QR",false,()->connectToPc("")));
        if(!exists)root.addView(Ui.button(this,"Restore from cloud · legacy recovery code",false,()->joinCloud("")));
        Ui.gap(root,16);String installedVersion="";try{installedVersion=AndroidUpdater.installed(this).versionName;}catch(Exception ignored){}
        root.addView(Ui.text(this,"Lockvyn v"+installedVersion+" · works offline\nA lost passphrase cannot be reset. Keep an encrypted backup.",11,Ui.MUTED,false));
        if(pendingImport!=null)handler.post(()->importPrompt(pendingImport));
    }
    private void showVault(){
        if(!engine.isOpen()||!active)return;
        if(scannedSecure!=null){String code=scannedSecure;scannedSecure=null;handler.post(()->connectToPc(code));}lastTouch=SystemClock.elapsedRealtime();list=null;entryAdapter=null;displayedEntries.clear();root=Ui.fixedScreen(this);
        LinearLayout top=new LinearLayout(this);top.setGravity(Gravity.CENTER_VERTICAL);top.addView(Ui.text(this,"Lockvyn",21,Ui.INK,true),new LinearLayout.LayoutParams(0,-2,1));
        TextView lock=Ui.text(this,"Lock",13,Ui.PURPLE,true);lock.setPadding(Ui.dp(this,12),Ui.dp(this,12),Ui.dp(this,12),Ui.dp(this,12));lock.setOnClickListener(v->lock());top.addView(lock);root.addView(top);
        LinearLayout heading=new LinearLayout(this);heading.setGravity(Gravity.CENTER_VERTICAL);
        String title=filter.equals("trash")?"Your trash":filter.equals("favorites")?"Your favorites":filter.equals("health")?"Review":"Your Vault";
        heading.addView(Ui.text(this,title,27,Ui.INK,true),new LinearLayout.LayoutParams(0,-2,1));
        Button options=Ui.button(this,"Tools",false,this::options);
        LinearLayout.LayoutParams toolParams=new LinearLayout.LayoutParams(-2,Ui.dp(this,48));
        toolParams.leftMargin=Ui.dp(this,10);heading.addView(options,toolParams);root.addView(heading);
        vaultStats=Ui.text(this,"",12,Ui.MUTED,false);Ui.gap(root,8);root.addView(vaultStats);
        Button add=Ui.button(this,"+ Add login",true,()->edit(null));
        add.setContentDescription("Add a new login to your encrypted vault");
        root.addView(add);Ui.gap(root,10);
        EditText find=Ui.field(root,"Search your vault",search,false,200);find.setHint("Name, username or website");find.addTextChangedListener(new TextWatcher(){public void beforeTextChanged(CharSequence s,int a,int c,int f){}public void onTextChanged(CharSequence s,int a,int b,int c){search=s.toString();renderList();}public void afterTextChanged(Editable e){}});
        LinearLayout tabs=new LinearLayout(this);
        String[] values={"all","favorites","health","trash"},labels={"All","Favorites","Review","Trash"};
        for(int i=0;i<4;i++){
            final String value=values[i];
            TextView chip=Ui.filterChip(this,labels[i],filter.equals(value),()->{filter=value;showVault();});
            LinearLayout.LayoutParams chipParams=new LinearLayout.LayoutParams(0,-2,1);
            chipParams.setMargins(Ui.dp(this,3),0,Ui.dp(this,3),0);
            tabs.addView(chip,chipParams);
        }
        root.addView(tabs);Ui.gap(root,8);
        try{
            List<String> names=new ArrayList<>();names.add("All folders");names.addAll(FolderCatalog.choices(engine.snapshot()));Spinner folders=new Spinner(this);
            folders.setAdapter(new ArrayAdapter<String>(this,android.R.layout.simple_spinner_dropdown_item,names));int selected=0;for(int i=1;i<names.size();i++)if(names.get(i).equalsIgnoreCase(folderFilter))selected=i;folders.setSelection(selected);root.addView(folders,new LinearLayout.LayoutParams(-1,Ui.dp(this,44)));
            folders.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener(){public void onItemSelected(AdapterView<?> p,View v,int pos,long id){folderFilter=pos==0?null:names.get(pos);renderList();}public void onNothingSelected(AdapterView<?> p){}});
        }catch(Exception e){Ui.error(this,e);}
        FrameLayout area=new FrameLayout(this);list=new ListView(this);list.setDivider(new android.graphics.drawable.ColorDrawable(Ui.BG));
        list.setDividerHeight(Ui.dp(this,8));list.setPadding(0,Ui.dp(this,6),0,0);list.setClipToPadding(false);area.addView(list,new FrameLayout.LayoutParams(-1,-1));
        TextView empty=Ui.text(this,"No logins here. Change the section, folder or search, or add a login above.",14,Ui.MUTED,false);empty.setGravity(Gravity.CENTER);empty.setPadding(Ui.dp(this,14),Ui.dp(this,14),Ui.dp(this,14),Ui.dp(this,14));area.addView(empty,new FrameLayout.LayoutParams(-1,-1));list.setEmptyView(empty);root.addView(area,new LinearLayout.LayoutParams(-1,0,1));
        entryAdapter=new BaseAdapter(){
            public int getCount(){return displayedEntries.size();}public Object getItem(int position){return displayedEntries.get(position);}public long getItemId(int position){return position;}
            public View getView(int position,View recycled,ViewGroup parent){
                LinearLayout row;TextView[] text;
                if(recycled==null){row=Ui.column(MainActivity.this);int pad=Ui.dp(MainActivity.this,15);row.setPadding(pad,pad,pad,pad);android.graphics.drawable.GradientDrawable surface=Ui.shape(Ui.WHITE,Ui.dp(MainActivity.this,14));
                    surface.setStroke(Math.max(1,Ui.dp(MainActivity.this,1)),Ui.BORDER);
                    row.setBackground(surface);text=new TextView[]{Ui.text(MainActivity.this,"",16,Ui.INK,true),Ui.text(MainActivity.this,"",12,Ui.MUTED,false),Ui.text(MainActivity.this,"",11,Ui.PURPLE,false)};for(TextView t:text){t.setSingleLine(true);t.setEllipsize(TextUtils.TruncateAt.END);row.addView(t);Ui.gap(row,5);}row.setTag(text);}else{row=(LinearLayout)recycled;text=(TextView[])row.getTag();}
                JSONObject e=displayedEntries.get(position);text[0].setText(e.optString("title")+(e.optBoolean("favorite")?"   ★":""));text[1].setText(e.optString("username"));text[2].setText(e.optString("folder"));return row;
            }
        };list.setAdapter(entryAdapter);list.setOnItemClickListener((parent,view,position,id)->{if(position<displayedEntries.size())detail(displayedEntries.get(position));});renderList();
        if(undoEntryId!=null&&SystemClock.elapsedRealtime()<undoUntil){String id=undoEntryId;Button undo=Ui.button(this,"Login moved to trash · Undo",false,()->task(()->{
            JSONArray entries=engine.snapshot().getJSONArray("entries");for(int i=0;i<entries.length();i++){JSONObject entry=entries.getJSONObject(i);if(entry.getString("id").equals(id)&&entry.getBoolean("deleted")&&!entry.optBoolean("purged")){JSONObject copy=new JSONObject(entry.toString());copy.put("deleted",false);engine.upsert(copy);return;}}
        },()->{undoEntryId=null;showVault();}));root.addView(undo);handler.postDelayed(()->{if(id.equals(undoEntryId)&&SystemClock.elapsedRealtime()>=undoUntil){undoEntryId=null;undo.setVisibility(View.GONE);}},Math.max(1,undoUntil-SystemClock.elapsedRealtime()));}
        syncStatus=Ui.text(this,"Cloud · checking when connected",11,Ui.MUTED,false);root.addView(syncStatus);
        LinearLayout nav=Ui.bottomNavigation(this,
            new String[]{"Vault","Sync","Backup","Settings"},
            new int[]{R.drawable.ic_nav_vault,R.drawable.ic_nav_sync,
                R.drawable.ic_nav_backup,R.drawable.ic_nav_settings},
            0,new Runnable[]{this::showVault,this::syncHub,this::backupHub,this::settings});
        root.addView(nav,new LinearLayout.LayoutParams(-1,-2));
    }
    private void renderList(){
        if(list==null||entryAdapter==null||!engine.isOpen())return;
        try{
            JSONArray entries=engine.snapshot().getJSONArray("entries");Set<String> pending=new HashSet<>();for(PasswordHealth.Finding f:PasswordHealth.audit(entries))if(!f.acknowledged)pending.add(f.id);int count=0;displayedEntries.clear();
            for(int i=0;i<entries.length();i++){
                JSONObject e=entries.getJSONObject(i);if(!e.getBoolean("deleted"))count++;if(e.getBoolean("purged")||e.getBoolean("deleted")!=filter.equals("trash"))continue;
                if(filter.equals("favorites")&&!e.getBoolean("favorite"))continue;if(filter.equals("health")&&!pending.contains(e.getString("id")))continue;if(folderFilter!=null&&!folderFilter.equalsIgnoreCase(e.getString("folder")))continue;
                String haystack=(e.getString("title")+" "+e.getString("username")+" "+e.getString("url")+" "+e.getString("folder")).toLowerCase(Locale.ROOT);if(!haystack.contains(search.toLowerCase(Locale.ROOT)))continue;displayedEntries.add(e);
            }
            displayedEntries.sort((a,b)->{int favorite=Boolean.compare(b.optBoolean("favorite"),a.optBoolean("favorite"));return favorite!=0?favorite:a.optString("title").compareToIgnoreCase(b.optString("title"));});
            vaultStats.setText(count+" saved logins · "+pending.size()+" to review · "+displayedEntries.size()+" shown");entryAdapter.notifyDataSetChanged();
        }catch(Exception e){Ui.error(this,e);}
    }
    private void options(){
        LinearLayout b=Ui.column(this);AlertDialog d=dialog("Vault tools",b);
        b.addView(Ui.button(this,"Generate password",false,()->{d.dismiss();generator();}));
        b.addView(Ui.button(this,"Folders",false,()->{d.dismiss();folders();}));
    }
    private void appUpdates(){
        if(busy){toast("Finish the current vault action before updating.");return;}
        task(engine::close,()->{for(AlertDialog d:new ArrayList<>(dialogs))clearCloudFields(d.getWindow().getDecorView());lock();startActivity(new Intent(this,UpdateActivity.class));});
    }
    private void folders(){
        try{
            LinearLayout b=Ui.column(this);b.addView(Ui.text(this,"Folders classify your logins independently of All, Favorites, Review and Trash. Choose a folder above the list, or assign one while editing.",13,Ui.MUTED,false));List<String> names=FolderCatalog.choices(engine.snapshot());Spinner select=new Spinner(this);ArrayAdapter<String> adapter=new ArrayAdapter<>(this,android.R.layout.simple_spinner_dropdown_item,names);select.setAdapter(adapter);b.addView(select);AlertDialog d=dialog("Folders",b);
            b.addView(Ui.button(this,"Show this folder",true,()->{folderFilter=(String)select.getSelectedItem();d.dismiss();showVault();}));
            b.addView(Ui.button(this,"Add a new folder",false,()->addFolder(name->{if(!names.contains(name)){names.add(name);adapter.notifyDataSetChanged();}select.setSelection(names.indexOf(name));showVault();})));
        }catch(Exception e){Ui.error(this,e);}
    }
    private void addFolder(Consumer<String> added){
        LinearLayout b=Ui.column(this);EditText name=Ui.field(b,"New folder name","",false,100);AlertDialog d=dialog("New folder",b);
        b.addView(Ui.button(this,"Add folder",true,()->{try{String typed=FolderCatalog.normalize(name.getText().toString());String canonical=typed;for(String existing:FolderCatalog.choices(engine.snapshot()))if(existing.equalsIgnoreCase(typed)){canonical=existing;break;}final String value=canonical;task(()->engine.addFolder(value),()->{d.dismiss();added.accept(value);});}catch(Exception e){toast(e.getMessage());}}));
    }
    private AlertDialog dialog(String title,LinearLayout body){ScrollView scroll=new ScrollView(this);int p=Ui.dp(this,22);body.setPadding(p,p,p,p);scroll.addView(body);AlertDialog d=new AlertDialog.Builder(this).setTitle(title).setView(scroll).setNegativeButton("Close",null).create();dialogs.add(d);d.setOnDismissListener(x->dialogs.remove(d));d.show();d.getWindow().setFlags(WindowManager.LayoutParams.FLAG_SECURE,WindowManager.LayoutParams.FLAG_SECURE);
        // Some forms add fields after showing. AlertDialog otherwise marks the initially
        // non-editable window ALT_FOCUSABLE_IM and prevents the soft keyboard.
        d.getWindow().clearFlags(WindowManager.LayoutParams.FLAG_ALT_FOCUSABLE_IM);d.getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);return d;}
    private void detail(JSONObject entry){
        try{
            LinearLayout b=Ui.column(this);b.addView(Ui.text(this,entry.getString("url").isEmpty()?"No website saved":VaultEngine.host(entry.getString("url")),12,Ui.MUTED,false));b.addView(Ui.text(this,"Folder: "+entry.getString("folder"),12,Ui.PURPLE,false));Ui.gap(b,16);
            b.addView(Ui.text(this,"Username / email",12,Ui.MUTED,false));b.addView(Ui.text(this,entry.getString("username"),16,Ui.INK,false));b.addView(Ui.button(this,"Copy username",false,()->copy(entry.optString("username"))));Ui.gap(b,15);TextView password=Ui.text(this,"••••••••••••••••",20,Ui.INK,false);b.addView(password);
            b.addView(Ui.button(this,"Show for 15 seconds",false,()->{password.setText(entry.optString("password"));handler.postDelayed(()->password.setText("••••••••••••••••"),15000);}));b.addView(Ui.button(this,"Copy password",true,()->copy(entry.optString("password"))));Ui.gap(b,15);b.addView(Ui.text(this,entry.getString("notes"),13,Ui.INK,false));AlertDialog d=dialog(entry.getString("title"),b);
            b.addView(Ui.button(this,"Edit login",false,()->{d.dismiss();edit(entry);}));if(!entry.getString("url").isEmpty())b.addView(Ui.button(this,"Open website",false,()->startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse(entry.optString("url"))))));
            boolean deleted=entry.getBoolean("deleted");b.addView(Ui.button(this,deleted?"Restore login":"Move to trash",false,()->task(()->{JSONObject copy=new JSONObject(entry.toString());copy.put("deleted",!deleted);engine.upsert(copy);},()->{undoEntryId=deleted?null:entry.optString("id");undoUntil=SystemClock.elapsedRealtime()+15000;d.dismiss();showVault();})));
            if(deleted)b.addView(Ui.button(this,"Delete permanently",false,()->new AlertDialog.Builder(this).setTitle("Delete permanently?").setMessage("Remove this login from the vault with no restore option? Older exported backups and unsynced devices may still hold their previous encrypted copy.").setNegativeButton("Cancel",null).setPositiveButton("Delete permanently",(x,w)->task(()->engine.deletePermanently(entry.getString("id")),()->{clearClipboard();d.dismiss();toast("Permanently deleted. Sync your other devices.");showVault();})).show()));
            if(!deleted)for(PasswordHealth.Finding f:PasswordHealth.audit(engine.snapshot().getJSONArray("entries")))if(f.id.equals(entry.getString("id"))){
                b.addView(Ui.text(this,(f.acknowledged?"Reviewed: ":"Review: ")+String.join("; ",f.reasons),12,Ui.MUTED,false));
                if(!f.acknowledged)b.addView(Ui.button(this,"I reviewed this login",false,()->new AlertDialog.Builder(this).setTitle("Confirm review").setMessage("I checked the warnings and accept this password for this login. Remove it from Review? Warnings return if the password or its risks change.").setNegativeButton("Cancel",null).setPositiveButton("Confirm review",(x,w)->task(()->engine.acknowledgeReview(entry.getString("id")),()->{d.dismiss();showVault();})).show()));
            }
        }catch(Exception e){Ui.error(this,e);}
    }
    private void edit(JSONObject original){
        try{
            JSONObject entry=original==null?VaultEngine.blank():new JSONObject(original.toString());LinearLayout page=Ui.column(this),body=Ui.column(this);int p=Ui.dp(this,20);page.setPadding(p,p,p,p);body.setPadding(0,Ui.dp(this,12),0,Ui.dp(this,16));
            page.addView(Ui.text(this,original==null?"Add a new login":"Edit login",24,Ui.INK,true));
            EditText title=Ui.field(body,"Name",entry.getString("title"),false,200),username=Ui.field(body,"Username / email",entry.getString("username"),false,2000),password=Ui.field(body,"Password",entry.getString("password"),true,4096);
            body.addView(Ui.button(this,"Generate password · choose rules",false,()->generator(value->password.setText(value))));
            EditText url=Ui.field(body,"Website (https://example.com)",entry.getString("url"),false,2048);
            LinearLayout optional=Ui.column(this);optional.setVisibility(View.GONE);
            optional.addView(Ui.text(this,"Folder",12,Ui.MUTED,false));List<String> names=FolderCatalog.choices(engine.snapshot());String current=entry.getString("folder");if(!names.contains(current)&&!current.isEmpty())names.add(current);Spinner folder=new Spinner(this);ArrayAdapter<String> adapter=new ArrayAdapter<>(this,android.R.layout.simple_spinner_dropdown_item,names);folder.setAdapter(adapter);folder.setSelection(Math.max(0,names.indexOf(current)));optional.addView(folder);
            optional.addView(Ui.button(this,"+ New folder",false,()->addFolder(name->{if(!names.contains(name)){names.add(name);adapter.notifyDataSetChanged();}folder.setSelection(names.indexOf(name));})));
            EditText notes=Ui.field(optional,"Private notes",entry.getString("notes"),false,32000);notes.setSingleLine(false);notes.setMinLines(2);notes.setMaxLines(6);CheckBox favorite=new CheckBox(this);favorite.setText("Add to favorites");favorite.setChecked(entry.getBoolean("favorite"));optional.addView(favorite);
            body.addView(Ui.button(this,"Folder, notes & favorite ▾",false,()->optional.setVisibility(optional.getVisibility()==View.GONE?View.VISIBLE:View.GONE)));body.addView(optional);
            ScrollView scroll=new ScrollView(this);scroll.setFillViewport(false);scroll.addView(body);page.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
            LinearLayout footer=Ui.column(this);page.addView(footer);AlertDialog d=new AlertDialog.Builder(this).setView(page).create();dialogs.add(d);d.setOnDismissListener(x->{dialogs.remove(d);password.setText("");username.setText("");notes.setText("");});
            footer.addView(Ui.button(this,"Save login",true,()->{
                String name=title.getText().toString().trim(),website=url.getText().toString().trim();
                if(name.isEmpty()){title.setError("Give this login a name.");title.requestFocus();return;}
                try{if(!website.isEmpty())VaultEngine.host(website);}catch(Exception invalid){url.setError(invalid.getMessage());url.requestFocus();return;}
                try{entry.put("title",name).put("username",username.getText().toString()).put("password",password.getText().toString()).put("url",website).put("folder",FolderCatalog.normalize((String)folder.getSelectedItem())).put("notes",notes.getText().toString()).put("favorite",favorite.isChecked());task(()->engine.upsert(entry),()->{d.dismiss();showVault();});}catch(Exception failure){Ui.error(this,failure);}
            }));footer.addView(Ui.button(this,"Cancel",false,d::dismiss));
            d.show();d.getWindow().setLayout(-1,-1);d.getWindow().setFlags(WindowManager.LayoutParams.FLAG_SECURE,WindowManager.LayoutParams.FLAG_SECURE);d.getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);
        }catch(Exception e){Ui.error(this,e);}
    }
    private void generator(){generator(null);}
    private void generator(Consumer<String> use){
        PasswordOptions defaults=GeneratorPreferences.load(this);LinearLayout b=Ui.column(this);b.addView(Ui.text(this,"Default length starts at 24. Choose the website's required rules; 16 or more characters are recommended.",13,Ui.MUTED,false));
        EditText value=Ui.field(b,"Generated on this device","",false,128);value.setKeyListener(null);EditText length=Ui.field(b,"Length (8–128)",Integer.toString(defaults.length),false,3);length.setInputType(InputType.TYPE_CLASS_NUMBER);
        CheckBox lower=check(b,"Lowercase letters (a–z)",defaults.lowercase),upper=check(b,"Uppercase letters (A–Z)",defaults.uppercase),digits=check(b,"Digits (0–9)",defaults.digits),symbols=check(b,"Symbols",defaults.symbols),ambiguous=check(b,"Exclude ambiguous characters (0, O, 1, I, l)",defaults.excludeAmbiguous);
        EditText allowed=Ui.field(b,"Allowed symbols (only these will be used)",defaults.allowedSymbols,false,32),minDigits=Ui.field(b,"Minimum digits when enabled",Integer.toString(defaults.minimumDigits),false,3),minSymbols=Ui.field(b,"Minimum symbols when enabled",Integer.toString(defaults.minimumSymbols),false,3);minDigits.setInputType(InputType.TYPE_CLASS_NUMBER);minSymbols.setInputType(InputType.TYPE_CLASS_NUMBER);
        TextView status=Ui.text(this,"",12,Ui.MUTED,false);b.addView(status);
        Callable<PasswordOptions> read=()->{PasswordOptions o=new PasswordOptions();o.length=Integer.parseInt(length.getText().toString());o.lowercase=lower.isChecked();o.uppercase=upper.isChecked();o.digits=digits.isChecked();o.symbols=symbols.isChecked();o.excludeAmbiguous=ambiguous.isChecked();o.allowedSymbols=allowed.getText().toString();o.minimumDigits=Integer.parseInt(minDigits.getText().toString());o.minimumSymbols=Integer.parseInt(minSymbols.getText().toString());o.validate();return o;};
        TextWatcher changed=new TextWatcher(){public void beforeTextChanged(CharSequence s,int a,int c,int f){}public void onTextChanged(CharSequence s,int a,int c,int n){value.setText("");}public void afterTextChanged(Editable e){}};for(EditText field:new EditText[]{length,allowed,minDigits,minSymbols})field.addTextChangedListener(changed);for(CheckBox c:new CheckBox[]{lower,upper,digits,symbols,ambiguous})c.setOnCheckedChangeListener((button,enabled)->value.setText(""));
        try{value.setText(defaults.generate());}catch(Exception e){status.setText(e.getMessage());}AlertDialog d=dialog("Generate password",b);
        b.addView(Ui.button(this,"Generate again",false,()->{try{value.setText(read.call().generate());status.setText("");}catch(Exception e){status.setText(e instanceof NumberFormatException?"Enter whole numbers for length and minimum counts.":e.getMessage());}}));
        b.addView(Ui.button(this,use==null?"Copy password":"Use this password",true,()->{try{if(value.getText().length()==0)value.setText(read.call().generate());if(use==null)copy(value.getText().toString());else{use.accept(value.getText().toString());d.dismiss();}}catch(Exception e){status.setText(e instanceof NumberFormatException?"Enter whole numbers for length and minimum counts.":e.getMessage());}}));
        b.addView(Ui.button(this,"Save these rules as my default",false,()->{try{GeneratorPreferences.save(this,read.call());status.setText("Default rules saved on this device.");}catch(Exception e){status.setText(e instanceof NumberFormatException?"Enter whole numbers for length and minimum counts.":e.getMessage());}}));d.setOnDismissListener(x->{value.setText("");dialogs.remove(d);});
    }
    private CheckBox check(LinearLayout body,String label,boolean enabled){CheckBox c=new CheckBox(this);c.setText(label);c.setChecked(enabled);body.addView(c);return c;}
    private void copy(String value){ClipboardManager c=(ClipboardManager)getSystemService(CLIPBOARD_SERVICE);ClipData data=ClipData.newPlainText("Lockvyn",value);if(Build.VERSION.SDK_INT>=33){PersistableBundle extras=new PersistableBundle();extras.putBoolean(ClipDescription.EXTRA_IS_SENSITIVE,true);data.getDescription().setExtras(extras);}c.setPrimaryClip(data);clipboard=value;clipboardUntil=SystemClock.elapsedRealtime()+20000;handler.postDelayed(()->{if(SystemClock.elapsedRealtime()>=clipboardUntil)clearClipboard();},20000);toast("Copied. Clipboard clears in 20 seconds. Paste into the intended app.");}
    private void clearClipboard(){try{ClipboardManager c=(ClipboardManager)getSystemService(CLIPBOARD_SERVICE);ClipData d=c.getPrimaryClip();if(clipboard!=null&&d!=null&&d.getItemCount()>0&&clipboard.contentEquals(d.getItemAt(0).coerceToText(this)))c.clearPrimaryClip();clipboard=null;}catch(Exception ignored){}}
    private void devices(){syncHub();} // Preserve old navigation callbacks without duplicating screens.

    private void syncHub(){
        if(!engine.isOpen()||busy)return;
        LinearLayout b=Ui.column(this);
        final AlertDialog[] hub={null};
        boolean connected=false;String email="",last="";
        JSONObject local=null;
        try{
            local=new CloudSettingsStore(this).load(engine.snapshot().getString("vaultId"));
            if(local!=null){connected=true;email=local.optString("email");last=local.optString("lastSyncUtc");}
        }catch(Exception ignored){}finally{CloudSettingsStore.clear(local);}
        LinearLayout card=Ui.card(this,connected?Ui.GREEN:Ui.SOFT);
        card.addView(Ui.text(this,connected?"● Cloud connected":"○ Cloud not connected",18,Ui.INK,true));
        card.addView(Ui.text(this,connected?"Account: "+email+"\nAutomatic encrypted sync is available.":"Connect once to sync logins securely between your devices.",13,Ui.MUTED,false));
        if(connected)card.addView(Ui.text(this,last.isEmpty()?"No successful sync yet.":"Last synced: "+last,12,Ui.MUTED,false));
        b.addView(card);
        b.addView(Ui.button(this,"Cloud Sync · status & sync now",true,()->{if(hub[0]!=null)hub[0].dismiss();cloud();}));
        b.addView(Ui.button(this,"Add new device · scan secure QR",false,()->{if(hub[0]!=null)hub[0].dismiss();connectToPc("");}));
        b.addView(Ui.text(this,"On Windows choose Sync & Devices → Add new device. Scan the temporary QR, compare six digits and approve on the PC.",12,Ui.MUTED,false));
        LinearLayout advanced=Ui.column(this);advanced.setVisibility(View.GONE);
        advanced.addView(Ui.button(this,"Transfer over private Wi-Fi (offline)",false,()->{if(hub[0]!=null)hub[0].dismiss();showPairing(true);}));
        advanced.addView(Ui.text(this,"Only use offline Wi-Fi transfer when cloud sync is unavailable. Your vault remains encrypted during transfer.",12,Ui.MUTED,false));
        b.addView(Ui.button(this,"Offline transfer & advanced options ▾",false,
            ()->advanced.setVisibility(advanced.getVisibility()==View.GONE?View.VISIBLE:View.GONE)));
        b.addView(advanced);
        hub[0]=dialog("Sync & Devices",b);
    }

    private void backupHub(){
        if(!engine.isOpen()||busy)return;
        LinearLayout b=Ui.column(this);
        b.addView(Ui.text(this,"Export a private encrypted backup to a location you control. Import safely merges a backup instead of silently overwriting your current vault.",14,Ui.MUTED,false));
        b.addView(Ui.button(this,"Export encrypted backup",true,()->{
            try{exportBytes=engine.export();startActivityForResult(new Intent(Intent.ACTION_CREATE_DOCUMENT).setType("application/octet-stream")
                .addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_TITLE,"Lockvyn.boshavault"),11);}
            catch(Exception e){Ui.error(this,e);}
        }));
        b.addView(Ui.button(this,"Import / restore encrypted backup",false,this::chooseImport));
        b.addView(Ui.text(this,"Save a copy away from this phone. A lost master passphrase cannot be recovered by support.",12,Ui.MUTED,false));
        dialog("Backup & Restore",b);
    }

    private void cloudTask(TextView status,Work action,Runnable done){
        if(busy){toast("A vault operation is already running.");return;}
        status.setText("Working… Keep this window open.");String[] failure={null};
        task(()->{try{action.run();}catch(Exception e){failure[0]=e instanceof IOException?"Cloud could not be reached. Your local vault is preserved. Try again when connected.":e.getMessage();}},()->{if(failure[0]!=null)status.setText(failure[0]);else done.run();});
    }
    private static void clearCloudFields(android.view.View view){
        if(view instanceof EditText)((EditText)view).setText("");
        if(view instanceof android.view.ViewGroup){android.view.ViewGroup group=(android.view.ViewGroup)view;for(int i=0;i<group.getChildCount();i++)clearCloudFields(group.getChildAt(i));}
    }
    interface CloudSessionReady {void accept(CloudAuth.Session session);}
    private void cloudAssure(CloudClient client,CloudAuth.Session session,JSONObject profile,CloudSettingsStore store,TextView status,boolean freshCode,CloudSessionReady ready){
        CloudAuth.Factors[] factors={null};
        cloudTask(status,()->factors[0]=client.auth.factors(session),()->{
            if(!factors[0].requiresChallenge(session)&&!freshCode){ready.accept(session);return;}
            if(factors[0].totp.isEmpty()){status.setText("This account requires an authenticator method this version cannot use.");return;}
            cloudVerify(client,session,profile,store,factors[0].totp,ready);
        });
    }
    private void cloudVerify(CloudClient client,CloudAuth.Session session,JSONObject profile,CloudSettingsStore store,List<CloudAuth.Factor> factors,CloudSessionReady ready){
            LinearLayout b=Ui.column(this);b.addView(Ui.text(this,"Enter a current six-digit authenticator code. Cloud access waits for verification.",13,Ui.MUTED,false));
            Spinner device=new Spinner(this);device.setAdapter(new ArrayAdapter<CloudAuth.Factor>(this,android.R.layout.simple_spinner_dropdown_item,factors));b.addView(device);
            EditText code=Ui.field(b,"Authenticator code","",true,6);code.setInputType(InputType.TYPE_CLASS_NUMBER|InputType.TYPE_NUMBER_VARIATION_PASSWORD);
            TextView result=Ui.text(this,"Ready.",13,Ui.MUTED,false);AlertDialog d=dialog("Verify cloud 2FA",b);
            b.addView(Ui.button(this,"Verify & continue",true,()->{
                String digits=code.getText().toString().trim(),id=((CloudAuth.Factor)device.getSelectedItem()).id;code.setText("");CloudAuth.Session[] next={null};
                cloudTask(result,()->{next[0]=client.auth.verify(session,id,digits);if(profile!=null)client.saveSession(next[0],profile,store);},()->{
                    if(!d.isShowing())return;d.dismiss();ready.accept(next[0]);
                });
            }));
            b.addView(Ui.button(this,"Cancel",false,d::dismiss));b.addView(result);
            d.setOnDismissListener(x->{dialogs.remove(d);clearCloudFields(b);});
    }
    private void cloudRefresh(CloudClient client,JSONObject profile,CloudSettingsStore store,TextView status,CloudSessionReady ready){
        CloudAuth.Session[] signedIn={null};cloudTask(status,()->signedIn[0]=client.refresh(profile,store),()->cloudAssure(client,signedIn[0],profile,store,status,false,ready));
    }
    private void cloudSecurity(CloudClient client,CloudAuth.Session session,JSONObject profile,CloudSettingsStore store){
        LinearLayout b=Ui.column(this);TextView status=Ui.text(this,"Loading authenticators…",13,Ui.MUTED,false);b.addView(status);AlertDialog d=dialog("Cloud account security · 2FA",b);
        d.setOnDismissListener(x->{dialogs.remove(d);clearCloudFields(b);});CloudAuth.Factors[] factors={null};
        cloudTask(status,()->factors[0]=client.auth.factors(session),()->{
            if(!d.isShowing())return;
            b.addView(Ui.text(this,factors[0].hasVerified?"2FA is enabled for this cloud account.":"2FA is not enabled yet.",16,Ui.INK,true));
            b.addView(Ui.text(this,"Use a TOTP app such as Google Authenticator or Aegis. Back up your authenticator or add another one. Losing all authenticators locks cloud access; your offline vault still works.",13,Ui.MUTED,false));
            b.addView(Ui.text(this,"After switching to your authenticator, unlock Lockvyn and choose Finish setup to enter its current code.",12,Ui.MUTED,false));
            for(CloudAuth.Factor pending:factors[0].all)if(!pending.verified&&pending.type.equals("totp"))b.addView(Ui.button(this,"Finish setup · "+pending.name,false,()->cloudVerify(client,session,profile,store,Collections.singletonList(pending),verified->{if(!d.isShowing())return;d.dismiss();cloudSecurity(client,verified,profile,store);})));
            for(CloudAuth.Factor factor:factors[0].totp)b.addView(Ui.button(this,"Remove "+factor.name,false,()->{
                LinearLayout confirm=Ui.column(this);confirm.addView(Ui.text(this,"Remove this authenticator? Removing the last one turns off 2FA. A current authenticator code is required.",14,Ui.INK,false));AlertDialog question=dialog("Remove authenticator",confirm);
                confirm.addView(Ui.button(this,"Continue",true,()->{question.dismiss();cloudAssure(client,session,profile,store,status,true,verified->{
                    CloudAuth.Session[] next={null};cloudTask(status,()->{client.auth.unenroll(verified,factor.id);next[0]=client.refresh(profile,store);},()->{if(!d.isShowing())return;d.dismiss();cloudSecurity(client,next[0],profile,store);});
                });}));confirm.addView(Ui.button(this,"Cancel",false,question::dismiss));
            }));
            b.addView(Ui.button(this,factors[0].hasVerified?"Add another authenticator":"Enable 2FA",true,()->cloudAssure(client,session,profile,store,status,false,verified->{
                CloudAuth.Enrollment[] enrollment={null};cloudTask(status,()->enrollment[0]=client.auth.enroll(verified,profile.getString("email")),()->{
                    if(!d.isShowing()){enrollment[0].close();return;}cloudEnrollment(client,verified,enrollment[0],profile,store,()->{d.dismiss();cloudRefresh(client,profile,store,status,updated->cloudSecurity(client,updated,profile,store));});
                });
            })));
            b.addView(Ui.button(this,"Close",false,d::dismiss));status.setText("Ready.");
        });
    }
    private void cloudEnrollment(CloudClient client,CloudAuth.Session session,CloudAuth.Enrollment enrollment,JSONObject profile,CloudSettingsStore store,Runnable finished){
        LinearLayout b=Ui.column(this);b.addView(Ui.text(this,"Scan this QR in your authenticator, or enter the setup key manually. Account: "+profile.optString("email")+". Time based, 6 digits, 30 seconds.",13,Ui.MUTED,false));
        final android.graphics.Bitmap[] bitmap={null};android.widget.ImageView image=new android.widget.ImageView(this);
        try{
            com.google.zxing.common.BitMatrix bits=new com.google.zxing.MultiFormatWriter().encode(enrollment.uri,com.google.zxing.BarcodeFormat.QR_CODE,480,480);
            int[] pixels=new int[480*480];for(int y=0;y<480;y++)for(int x=0;x<480;x++)pixels[y*480+x]=bits.get(x,y)?0xff000000:0xffffffff;
            // The int[] overload creates an immutable bitmap, which cannot be erased on close.
            bitmap[0]=android.graphics.Bitmap.createBitmap(480,480,android.graphics.Bitmap.Config.ARGB_8888);bitmap[0].setPixels(pixels,0,480,0,0,480,480);Arrays.fill(pixels,0);image.setImageBitmap(bitmap[0]);
            LinearLayout.LayoutParams qr=new LinearLayout.LayoutParams(Ui.dp(this,240),Ui.dp(this,240));qr.gravity=android.view.Gravity.CENTER_HORIZONTAL;b.addView(image,qr);
        }catch(Exception e){b.addView(Ui.text(this,"QR could not be rendered. Use the setup key below.",12,Ui.MUTED,false));}
        EditText key=Ui.field(b,"Setup key · keep private",enrollment.secret,false,128);key.setKeyListener(null);key.setTextIsSelectable(true);
        EditText code=Ui.field(b,"Current authenticator code","",true,6);code.setInputType(InputType.TYPE_CLASS_NUMBER|InputType.TYPE_NUMBER_VARIATION_PASSWORD);
        b.addView(Ui.text(this,"2FA becomes active only after verification. Save an encrypted authenticator backup before finishing.",12,Ui.MUTED,false));TextView status=Ui.text(this,"Ready.",13,Ui.MUTED,false);AlertDialog d=dialog("Set up Lockvyn 2FA",b);
        b.addView(Ui.button(this,"Verify code & enable",true,()->{
            String digits=code.getText().toString().trim();code.setText("");
            cloudTask(status,()->client.saveSession(client.auth.verify(session,enrollment.id,digits),profile,store),()->{if(!d.isShowing())return;d.dismiss();finished.run();});
        }));
        b.addView(Ui.button(this,"Cancel setup",false,()->cloudTask(status,()->client.auth.unenroll(session,enrollment.id),()->{if(d.isShowing())d.dismiss();})));
        b.addView(status);d.setOnDismissListener(x->{dialogs.remove(d);clearCloudFields(b);image.setImageDrawable(null);if(bitmap[0]!=null){bitmap[0].eraseColor(0);bitmap[0].recycle();bitmap[0]=null;}enrollment.close();});
    }
    private void cloud(){
        if(!engine.isOpen()||busy)return;
        autoSession=null;
        cloudOpen=true;
        final CloudSettingsStore store=new CloudSettingsStore(this);final int stamp=epoch;
        final CloudClient client=new CloudClient(()->{if(!active||stamp!=epoch||!engine.isOpen())throw new IOException("Cloud operation cancelled.");});
        JSONObject saved=null;String vaultId;
        try{vaultId=engine.snapshot().getString("vaultId");saved=store.load(vaultId);}catch(Exception e){
            LinearLayout b=Ui.column(this);b.addView(Ui.text(this,"Saved cloud settings could not be opened. Your local vault is available.",13,Ui.MUTED,false));AlertDialog d=dialog("Encrypted cloud sync",b);
            b.addView(Ui.button(this,"Forget local cloud settings",false,()->{try{store.forget();d.dismiss();cloud();}catch(Exception error){Ui.error(this,error);}}));d.setOnDismissListener(x->{dialogs.remove(d);cloudOpen=false;nextCloudCheck=0;});client.close();return;
        }
        final JSONObject profile=saved;final String localVaultId=vaultId;LinearLayout b=Ui.column(this);TextView status=Ui.text(this,"Ready. Your vault stays usable without Internet.",13,Ui.MUTED,false);
        AlertDialog d=dialog("Encrypted cloud sync",b);
        d.setOnDismissListener(x->{dialogs.remove(d);client.close();clearCloudFields(b);cloudOpen=false;nextCloudCheck=0;worker.execute(()->CloudSettingsStore.clear(profile));});
        if(profile!=null){
            b.addView(Ui.text(this,"Connected to "+profile.optString("email")+". New logins sync automatically when connected. Unlock to apply incoming changes.",13,Ui.MUTED,false));
            b.addView(Ui.text(this,profile.optString("lastSyncUtc").isEmpty()?"No successful sync yet.":"Last successful sync: "+profile.optString("lastSyncUtc"),12,Ui.MUTED,false));
            CheckBox auto=check(b,"Sync automatically on this device",profile.optBoolean("autoSync",true));auto.setOnCheckedChangeListener((button,enabled)->cloudTask(status,()->{synchronized(CloudClient.GATE){JSONObject latest=store.load(profile.getString("vaultId"));if(latest==null)throw new IOException("Cloud is disconnected on this device.");try{latest.put("autoSync",enabled);store.save(latest);profile.put("autoSync",enabled);}finally{CloudSettingsStore.clear(latest);}}},()->{status.setText(enabled?"Automatic sync is on.":"Automatic sync is paused on this device.");if(enabled)nextCloudCheck=0;}));
            b.addView(Ui.button(this,"Sync now",true,()->cloudRefresh(client,profile,store,status,signedIn->{int[] conflicts={0};cloudTask(status,()->conflicts[0]=client.sync(engine,profile,store,signedIn),()->{status.setText("Synced successfully. "+conflicts[0]+" conflict copies kept.");renderList();});})));
            b.addView(Ui.button(this,"Connect to my PC · secure QR",false,()->{d.dismiss();connectToPc("");}));
            LinearLayout advancedCloud=Ui.column(this);advancedCloud.setVisibility(View.GONE);
            b.addView(Ui.button(this,"Security & advanced cloud options ▾",false,()->advancedCloud.setVisibility(
                advancedCloud.getVisibility()==View.GONE?View.VISIBLE:View.GONE)));
            b.addView(advancedCloud);
            advancedCloud.addView(Ui.button(this,"Cloud account security · 2FA",false,()->cloudRefresh(client,profile,store,status,signedIn->cloudSecurity(client,signedIn,profile,store))));
            advancedCloud.addView(Ui.button(this,"Copy legacy recovery code · keep private",false,()->{try{copy(CloudSnapshot.pairing(profile));status.setText("Permanent cloud recovery key copied; keep private. The clipboard clears in 20 seconds.");}catch(Exception e){Ui.error(this,e);}}));
            advancedCloud.addView(Ui.text(this,"This legacy recovery code contains a permanent key. Normally use secure QR pairing instead.",12,Ui.MUTED,false));
            advancedCloud.addView(Ui.button(this,"Disconnect this device",false,()->new AlertDialog.Builder(this).setTitle("Disconnect cloud?").setMessage("Keep your local vault and encrypted cloud backups, and sign out this cloud session? Keep your connection code on another trusted device first.").setNegativeButton("Cancel",null).setPositiveButton("Disconnect",(x,y)->cloudRefresh(client,profile,store,status,signedIn->cloudTask(status,()->client.signOut(signedIn,store),()->{d.dismiss();toast("Cloud session signed out. Previously issued access tokens expire normally. Local vault unchanged.");}))).show()));
            advancedCloud.addView(Ui.button(this,"Forget local cloud settings (offline)",false,()->new AlertDialog.Builder(this).setTitle("Forget cloud settings?").setMessage("This forgets your recovery key and saved sign-in on this phone. It does not revoke server sessions or remove cloud backups. Keep your code elsewhere first.").setNegativeButton("Cancel",null).setPositiveButton("Forget",(x,y)->cloudTask(status,store::forget,()->{d.dismiss();toast("Local settings forgotten; server session not revoked.");})).show()));
        }else{
            b.addView(Ui.text(this,"Create a Lockvyn cloud account here with your own email. Each person uses their own account; sign in with that same account on your own devices.",13,Ui.MUTED,false));
            b.addView(Ui.text(this,"Your offline vault needs no account. The cloud password is separate from your master passphrase.",12,Ui.MUTED,false));
            EditText email=Ui.field(b,"Cloud account email","",false,320),password=Ui.field(b,"Separate cloud password (16+ characters)","",true,1024);
            email.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS);
            LinearLayout accountAdvanced=Ui.column(this);accountAdvanced.setVisibility(View.GONE);
            EditText code=Ui.field(accountAdvanced,"Other device's cloud recovery code · advanced","",true,1024);
            EditText confirmation=Ui.field(accountAdvanced,"Email confirmation code (if shown in your email)","",true,10);confirmation.setInputType(InputType.TYPE_CLASS_NUMBER|InputType.TYPE_NUMBER_VARIATION_PASSWORD);
            b.addView(Ui.text(this,"First device: create and confirm a cloud account, then start a new cloud vault. To join this vault from a connected PC, choose Connect to my PC and scan its temporary QR.",12,Ui.MUTED,false));
            b.addView(Ui.button(this,"Connect to my PC · scan secure QR",true,()->{d.dismiss();connectToPc("");}));
            b.addView(Ui.button(this,"Create cloud account",false,()->{String secret=password.getText().toString(),address=email.getText().toString().trim();password.setText("");cloudTask(status,()->client.signUp(address,secret),()->status.setText("Confirm the account using your email, then sign in below. Local vault unchanged."));}));
            accountAdvanced.addView(Ui.button(this,"Resend confirmation email",false,()->{String address=email.getText().toString().trim();cloudTask(status,()->client.resendConfirmation(address),()->status.setText("If the account is awaiting confirmation, check your email including Spam. Wait at least 60 seconds before requesting another message."));}));
            accountAdvanced.addView(Ui.button(this,"Confirm email code",false,()->{String digits=confirmation.getText().toString().trim(),address=email.getText().toString().trim();confirmation.setText("");cloudTask(status,()->client.auth.verifyEmail(address,digits),()->status.setText("Email confirmed. Enter your cloud password and sign in below. 2FA is checked separately."));}));
            for(boolean existing:new boolean[]{false,true})(existing?accountAdvanced:b).addView(Ui.button(this,existing?"Join this vault's existing cloud":"Start a new cloud vault",!existing,()->{
                String address=email.getText().toString().trim(),secret=password.getText().toString(),pairingCode=code.getText().toString().trim();password.setText("");code.setText("");
                CloudAuth.Session[] session={null};cloudTask(status,()->session[0]=client.signIn(address,secret),()->cloudAssure(client,session[0],null,store,status,false,signedIn->cloudTask(status,()->{
                    if(!active||stamp!=epoch)throw new IOException("Cloud operation cancelled.");
                    JSONObject pairing=existing?CloudSnapshot.pairing(pairingCode,localVaultId,signedIn.owner):null;
                    JSONObject next=new JSONObject().put("version",1).put("ownerId",signedIn.owner).put("vaultId",localVaultId).put("vaultSlot",pairing==null?UUID.randomUUID().toString():pairing.getString("vaultSlot")).put("deviceSlot",UUID.randomUUID().toString()).put("recoveryKey",pairing==null?CloudSnapshot.newKey():pairing.getString("recoveryKey")).put("refreshToken",signedIn.refresh).put("email",address).put("lastSyncUtc","").put("autoSync",true);
                    try{store.save(next);}finally{CloudSettingsStore.clear(next);}
                },()->{d.dismiss();cloud();})));
            }));
            b.addView(Ui.button(this,"Email verification & legacy recovery ▾",false,()->
                accountAdvanced.setVisibility(accountAdvanced.getVisibility()==View.GONE?View.VISIBLE:View.GONE)));
            b.addView(accountAdvanced);
        }
        Ui.gap(b,12);b.addView(status);
    }
    /** Guided, authenticated, user-approved pairing; no recovery key in the QR. */
    private void connectToPc(String invitation){
        if(busy)return;
        boolean existing=Store.vault(this).exists();
        if(existing&&!engine.isOpen()){scannedSecure=invitation.isEmpty()?null:invitation;toast("Unlock the existing vault first, then connect from Sync.");return;}
        LinearLayout b=Ui.column(this);
        b.addView(Ui.text(this,"On Windows open Sync → Add device. Scan its temporary QR; never share a permanent recovery code.",14,Ui.MUTED,false));
        b.addView(Ui.button(this,"Scan secure Add device QR",true,()->startActivityForResult(new Intent(this,ScanQrActivity.class).putExtra("securePairing",true),15)));
        if(invitation.isEmpty()){dialog("Connect to my PC",b);return;}
        final DevicePairing peer;
        try{peer=new DevicePairing(invitation);}catch(Exception error){Ui.error(this,error);return;}
        b.addView(Ui.text(this,"Secure QR scanned. Sign in using the SAME cloud account as Windows. The phone will show six digits to compare.",13,Ui.PURPLE,true));
        EditText email=Ui.field(b,"Cloud account email","",false,320),
            password=Ui.field(b,"Cloud account password","",true,1024);
        email.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_EMAIL_ADDRESS);
        EditText master=existing?null:Ui.field(b,"Existing Windows vault master passphrase","",true,1024);
        TextView number=Ui.text(this,"••• •••",26,Ui.INK,true);
        number.setGravity(Gravity.CENTER);b.addView(number);
        TextView status=Ui.text(this,"Ready. This pairing expires after two minutes.",13,Ui.MUTED,false);
        b.addView(status);
        int stamp=epoch; CloudSettingsStore store=new CloudSettingsStore(this);
        CloudClient client=new CloudClient(()->{if(!active||stamp!=epoch)throw new IOException("Pairing was interrupted.");});
        AlertDialog d=dialog("Connect to my PC · verify",b);
        final Runnable[] poll={null};
        final CloudAuth.Session[] authSession={null};
        final boolean[] finished={false};
        d.setOnDismissListener(x->{
            dialogs.remove(d);if(!finished[0])client.close();peer.close();
            clearCloudFields(b);cloudOpen=false;nextCloudCheck=0;
        });
        b.addView(Ui.button(this,"Sign in & request approval",true,()->{
            String address=email.getText().toString().trim(),secret=password.getText().toString();
            String passphrase=master==null?"":master.getText().toString();
            password.setText("");if(master!=null)master.setText("");
            cloudTask(status,()->authSession[0]=client.signIn(address,secret),()->{
                if(!d.isShowing())return;
                cloudAssure(client,authSession[0],null,store,status,false,verified->{
                    authSession[0]=verified;
                    cloudTask(status,()->client.pairingRequest(peer,verified),()->{
                        if(!d.isShowing())return;
                        try{
                            String sas=peer.securityCode();
                            number.setText(sas.substring(0,3)+" "+sas.substring(3));
                            status.setText("Compare this number with Windows. The PC owner must explicitly approve.");
                            poll[0]=new Runnable(){public void run(){
                                if(!d.isShowing()||finished[0])return;
                                if(System.currentTimeMillis()/1000>=peer.expiresUnix){status.setText("Pairing expired. Scan a new QR.");return;}
                                if(busy){handler.postDelayed(this,1500);return;}
                                String[] sealed={null};
                                cloudTask(status,()->sealed[0]=client.pairingSealed(peer,verified),()->{
                                    if(!d.isShowing())return;
                                    if(sealed[0]==null){
                                        status.setText("Waiting for Windows to approve the matching security code…");
                                        handler.postDelayed(poll[0],2000);return;
                                    }
                                    cloudTask(status,()->{
                                        String secretPairing=peer.open(sealed[0]);
                                        JSONObject paired=CloudSnapshot.pairing(secretPairing,existing?engine.snapshot().getString("vaultId"):null,verified.owner);
                                        if(existing){
                                            if(!engine.isOpen())throw new IOException("Unlock your vault before pairing.");
                                            JSONObject old=store.load(paired.getString("vaultId"));
                                            if(old!=null){CloudSettingsStore.clear(old);throw new Exception("This vault is already connected. Disconnect explicitly before changing cloud keys.");}
                                        }else if(Store.vault(MainActivity.this).exists())throw new IOException("Vault was created during pairing. No data was replaced.");
                                        byte[] inner=null;
                                        try{
                                            if(!existing){
                                                inner=client.firstSnapshot(paired,verified);
                                                VaultEngine.importNew(Store.vault(MainActivity.this),inner,passphrase,Store.device(MainActivity.this));
                                                engine.open(passphrase);engine.revokeTrust();
                                            }
                                            JSONObject profile=new JSONObject()
                                                .put("version",1).put("ownerId",verified.owner).put("vaultId",paired.getString("vaultId"))
                                                .put("vaultSlot",paired.getString("vaultSlot")).put("deviceSlot",UUID.randomUUID().toString())
                                                .put("recoveryKey",paired.getString("recoveryKey")).put("refreshToken",verified.refresh)
                                                .put("email",address).put("lastSyncUtc","").put("autoSync",true);
                                            try{store.save(profile);}finally{CloudSettingsStore.clear(profile);}
                                            client.pairingComplete(peer,verified);
                                        }finally{if(inner!=null)Arrays.fill(inner,(byte)0);}
                                    },()->{finished[0]=true;status.setText("Connected successfully. Automatic sync is on.");d.dismiss();showVault();});
                                });
                            }};
                            handler.postDelayed(poll[0],300);
                        }catch(Exception error){status.setText("Secure pairing failed: "+error.getMessage());}
                    });
                });
            });
        }));
        b.addView(Ui.button(this,"Cancel pairing",false,d::dismiss));
    }
    private void joinCloud(String scanned){
        if(Store.vault(this).exists()){toast("A vault already exists. Unlock and use Sync to connect it.");return;}
        int stamp=epoch;CloudSettingsStore store=new CloudSettingsStore(this);CloudClient client=new CloudClient(()->{if(!active||stamp!=epoch)throw new IOException("Cloud operation cancelled.");});
        LinearLayout b=Ui.column(this);b.addView(Ui.text(this,"Sign in with the same cloud account. Scan the connection QR on your own computer, then unlock its encrypted backup with your master passphrase. The passphrase stays on this phone.",13,Ui.MUTED,false));
        b.addView(Ui.button(this,"Scan cloud connection QR",false,()->startActivityForResult(new Intent(this,ScanQrActivity.class).putExtra("cloud",true),14)));
        EditText code=Ui.field(b,"Cloud connection code (BVCLOUD1:…)",scanned,false,1024);code.setSingleLine(false);code.setMaxLines(4);
        EditText email=Ui.field(b,"Cloud account email","",false,320),account=Ui.field(b,"Cloud account password","",true,1024),master=Ui.field(b,"Vault master passphrase","",true,1024);
        TextView status=Ui.text(this,"The first device must finish its initial sync before this step.",13,Ui.MUTED,false);b.addView(status);AlertDialog d=dialog("Restore encrypted cloud vault",b);
        d.setOnDismissListener(x->{dialogs.remove(d);client.close();clearCloudFields(b);});
        b.addView(Ui.button(this,"Sign in & restore",true,()->{
            String pairingCode=code.getText().toString().trim(),address=email.getText().toString().trim(),secret=account.getText().toString(),passphrase=master.getText().toString();account.setText("");master.setText("");
            CloudAuth.Session[] signed={null};cloudTask(status,()->signed[0]=client.signIn(address,secret),()->cloudAssure(client,signed[0],null,store,status,false,verified->cloudTask(status,()->{
                if(Store.vault(this).exists())throw new IOException("A local vault already exists.");JSONObject pairing=CloudSnapshot.pairing(pairingCode,null,verified.owner);
                byte[] inner=client.firstSnapshot(pairing,verified);
                try{VaultEngine.importNew(Store.vault(this),inner,passphrase,Store.device(this));engine.open(passphrase);engine.revokeTrust();
                    JSONObject profile=new JSONObject().put("version",1).put("ownerId",verified.owner).put("vaultId",pairing.getString("vaultId")).put("vaultSlot",pairing.getString("vaultSlot")).put("deviceSlot",UUID.randomUUID().toString()).put("recoveryKey",pairing.getString("recoveryKey")).put("refreshToken",verified.refresh).put("email",address).put("lastSyncUtc","").put("autoSync",true);
                    try{store.save(profile);}finally{CloudSettingsStore.clear(profile);}
                }finally{Arrays.fill(inner,(byte)0);}
            },()->{d.dismiss();showVault();})));
        }));
    }
    private void chooseImport(){startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE),12);}
    @Override protected void onActivityResult(int request,int result,Intent intent){super.onActivityResult(request,result,intent);if(request==13){scannedPairing="";if(result==RESULT_OK&&intent!=null){String code=intent.getStringExtra(ScanQrActivity.RESULT_PAIRING);try{new LocalTransfer(code);scannedPairing=code;}catch(Exception e){toast("The scanned code is not a valid Lockvyn pairing code.");}}return;}if(request==14){if(result==RESULT_OK&&intent!=null)scannedCloud=intent.getStringExtra(ScanQrActivity.RESULT_PAIRING);return;}if(request==15){if(result==RESULT_OK&&intent!=null)scannedSecure=intent.getStringExtra(ScanQrActivity.RESULT_PAIRING);return;}if(result!=RESULT_OK||intent==null||intent.getData()==null){exportBytes=null;return;}Uri uri=intent.getData();
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
        LinearLayout b=Ui.column(this);b.addView(Ui.text(this,"Start a private transfer in Windows and keep its window open. Connect both devices to the same local Wi-Fi or hotspot, then scan its current QR. Internet is not required.",13,Ui.MUTED,false));
        b.addView(Ui.button(this,"Scan QR code",true,()->{if(!busy)startActivityForResult(new Intent(this,ScanQrActivity.class),13);}));Ui.gap(b,18);
        EditText code=Ui.field(b,"Pairing code (BV1:…)",scanned,false,4096);code.setSingleLine(false);code.setMaxLines(4);code.setImportantForAutofill(View.IMPORTANT_FOR_AUTOFILL_NO);code.setSelectAllOnFocus(true);
        if(!scanned.isEmpty())b.addView(Ui.text(this,"QR scanned. Tap Connect & transfer to continue.",13,Ui.PURPLE,true));
        TextView status=Ui.text(this,"Ready to connect over local Wi-Fi.",13,Ui.MUTED,false);b.addView(status);
        boolean[] continueTransfer={false};java.util.concurrent.atomic.AtomicBoolean abandoned=new java.util.concurrent.atomic.AtomicBoolean();AlertDialog d=dialog("Connect your devices",b);
        d.setOnDismissListener(x->{clearCloudFields(b);dialogs.remove(d);if(!continueTransfer[0]){abandoned.set(true);cancelLocalTransfer();pendingImport=null;}});
        b.addView(Ui.button(this,"Connect & transfer",true,()->{
            if(busy)return;String c=code.getText().toString().trim();int stamp=epoch;
            task(()->{
                LocalTransfer connection=new LocalTransfer(c,(socket,host)->LocalWifi.bind(this,socket,host),message->handler.post(()->{if(active&&epoch==stamp&&d.isShowing()){status.setText(message);lastTouch=SystemClock.elapsedRealtime();}}));
                pendingTransfer=connection;
                try{
                    if(!active||epoch!=stamp||abandoned.get()){connection.cancel();throw new IOException("Transfer cancelled.");}
                    byte[] bytes=connection.download();if(!active||epoch!=stamp){connection.cancel();throw new IOException("Transfer cancelled.");}
                    pendingImport=bytes;
                    if(engine.isOpen()&&!engine.needsPassphrase(bytes)){engine.merge(bytes);connection.upload(engine.export());pendingImport=null;pendingTransfer=null;}
                }catch(Exception failure){connection.cancel();if(pendingTransfer==connection)pendingTransfer=null;pendingImport=null;throw failure;}
            },()->{continueTransfer[0]=true;d.dismiss();if(pendingImport==null){toast("Both devices have the merged vault.");showVault();}else pairingImportPrompt(pendingImport);});
        }));
        b.addView(Ui.button(this,"Cancel transfer",false,d::dismiss));
    }
    private void pairingImportPrompt(byte[] bytes){
        pendingImport=null;LocalTransfer connection=pendingTransfer;if(connection==null)return;boolean exists=Store.vault(this).exists();LinearLayout b=Ui.column(this);EditText master=Ui.field(b,exists?"Current phone passphrase":"Windows vault passphrase","",true,1024);EditText incoming=exists?Ui.field(b,"Incoming passphrase if rotated","",true,1024):null;b.addView(Ui.text(this,"Unlock the received vault locally. Keep the Windows transfer window open. No passphrase is sent over the network. If sending fails, your phone keeps its merged data.",13,Ui.MUTED,false));AlertDialog d=dialog("Complete the encrypted transfer",b);
        boolean[] finished={false};d.setOnDismissListener(x->{clearCloudFields(b);dialogs.remove(d);if(!finished[0])cancelLocalTransfer();});
        b.addView(Ui.button(this,"Unlock & complete transfer",true,()->{String p=master.getText().toString(),r=incoming==null?"":incoming.getText().toString();master.setText("");if(incoming!=null)incoming.setText("");int stamp=epoch;task(()->{if(!active||epoch!=stamp||pendingTransfer!=connection)throw new IOException("Transfer cancelled.");if(exists){if(!engine.isOpen())engine.open(p);if(engine.needsPassphrase(bytes)){engine.mergeUsingPassword(bytes,r.isEmpty()?p:r);Biometrics.disable(this);}else engine.merge(bytes);}else{VaultEngine.importNew(Store.vault(this),bytes,p,Store.device(this));engine.open(p);engine.revokeTrust();}connection.upload(engine.export());pendingTransfer=null;},()->{finished[0]=true;d.dismiss();toast("Transfer complete.");showVault();});}));
        b.addView(Ui.button(this,"Cancel transfer",false,d::dismiss));
    }
    private void settings(){
        if(!engine.isOpen()||busy)return;
        LinearLayout b=Ui.column(this);
        b.addView(Ui.text(this,"Security, Autofill and software updates.",14,Ui.MUTED,false));
        Ui.gap(b,12);
        LinearLayout provider=Ui.card(this,Ui.SOFT);
        provider.addView(Ui.text(this,"Android Autofill",18,Ui.INK,true));
        TextView providerStatus=Ui.text(this,AutofillStatus.summary(this),13,Ui.MUTED,false);
        provider.addView(providerStatus);b.addView(provider);
        b.addView(Ui.button(this,"Enable Android Autofill",true,()->{
            try{startActivity(new Intent(Settings.ACTION_REQUEST_SET_AUTOFILL_SERVICE,Uri.parse("package:"+getPackageName())));}
            catch(Exception e){startActivity(new Intent(Settings.ACTION_SETTINGS));}
        }));
        b.addView(Ui.button(this,Biometrics.available(this)?"Disable fingerprint unlock":"Enable fingerprint unlock",false,()->{
            try{
                if(Biometrics.available(this)){Biometrics.disable(this);toast("Fingerprint unlock disabled.");}
                else Biometrics.enable(this,engine.biometricKey(),(key,error)->{
                    if(error!=null)Ui.error(this,error);
                    else toast("Fingerprint unlock enabled for this phone.");
                });
            }catch(Exception e){Ui.error(this,e);}
        }));
        b.addView(Ui.button(this,"Software updates",false,this::appUpdates));
        b.addView(Ui.button(this,"Cloud account security · 2FA",false,()->cloud()));
        LinearLayout advanced=Ui.column(this);advanced.setVisibility(View.GONE);
        advanced.addView(Ui.text(this,"Troubleshooting & security options",16,Ui.INK,true));
        TextView lastRequest=Ui.text(this,AutofillStatus.diagnostic(this),12,Ui.MUTED,false);
        advanced.addView(lastRequest);
        advanced.addView(Ui.button(this,"Refresh Autofill status",false,()->{
            providerStatus.setText(AutofillStatus.summary(this));
            lastRequest.setText(AutofillStatus.diagnostic(this));
        }));
        advanced.addView(Ui.button(this,"Clear Autofill diagnostics",false,()->{
            AutofillStatus.clear(this);lastRequest.setText(AutofillStatus.diagnostic(this));
        }));
        advanced.addView(Ui.button(this,"Revoke all Autofill app approvals",false,
            ()->task(engine::revokeTrust,()->toast("Approvals revoked. Apps must be approved again."))));
        Ui.gap(advanced,16);
        EditText current=Ui.field(advanced,"Current master passphrase","",true,1024);
        EditText next=Ui.field(advanced,"New master passphrase (16+ characters)","",true,1024);
        EditText confirm=Ui.field(advanced,"Confirm new passphrase","",true,1024);
        advanced.addView(Ui.button(this,"Change master passphrase",false,()->{
            String oldPassword=current.getText().toString(),newPassword=next.getText().toString();
            if(!newPassword.equals(confirm.getText().toString())){toast("Passphrases do not match.");return;}
            current.setText("");next.setText("");confirm.setText("");
            task(()->{engine.changePassword(oldPassword,newPassword);Biometrics.disable(this);},
                 ()->toast("Passphrase changed. Older backups still use their original passphrase."));
        }));
        advanced.addView(Ui.text(this,"Your encrypted vault is not uploaded for Autofill. Keep a separate encrypted backup before changing master passphrases.",12,Ui.MUTED,false));
        b.addView(Ui.button(this,"Advanced security & troubleshooting ▾",false,
            ()->advanced.setVisibility(advanced.getVisibility()==View.GONE?View.VISIBLE:View.GONE)));
        b.addView(advanced);
        AlertDialog settingsDialog=dialog("Settings",b);
        settingsDialog.setOnDismissListener(x->{
            dialogs.remove(settingsDialog);
            current.setText("");next.setText("");confirm.setText("");
        });
    }
}
