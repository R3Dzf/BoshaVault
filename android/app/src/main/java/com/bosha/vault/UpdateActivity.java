package com.bosha.vault;

import android.app.*;
import android.content.*;
import android.net.Uri;
import android.os.Bundle;
import android.provider.Settings;
import android.widget.*;
import java.util.concurrent.*;

/** Explicit updates, with Android's permission and installer confirmation. */
public final class UpdateActivity extends Activity {
    private final AndroidUpdater updater=new AndroidUpdater();
    private final ExecutorService worker=Executors.newSingleThreadExecutor();
    private TextView status;private Button check,download,install;private ProgressBar progress;
    private AndroidUpdateFeed feed;private boolean visible,busy,ready,resumeInstall;
    @Override public void onCreate(Bundle state){super.onCreate(state);LinearLayout body=Ui.screen(this);Ui.gap(body,25);body.addView(Ui.brandIcon(this,64));body.addView(Ui.text(this,"Lockvyn updates",26,Ui.INK,true));
        String version="";try{version=AndroidUpdater.installed(this).versionName;}catch(Exception ignored){}body.addView(Ui.text(this,"Installed version: "+version,13,Ui.MUTED,false));Ui.gap(body,16);
        status=Ui.text(this,"Check for a stable release. Your vault stays on this phone.",14,Ui.INK,false);body.addView(status);progress=new ProgressBar(this,null,android.R.attr.progressBarStyleHorizontal);progress.setMax(100);progress.setVisibility(android.view.View.GONE);body.addView(progress);
        check=Ui.button(this,"Check for updates",true,this::check);download=Ui.button(this,"Download update",false,this::download);install=Ui.button(this,"Install update",true,this::install);body.addView(check);body.addView(download);body.addView(install);
        body.addView(Ui.button(this,"Cancel download",false,()->{if(busy){updater.cancel();status.setText("Cancelling…");}}));
        body.addView(Ui.button(this,"View releases",false,()->startActivity(new Intent(Intent.ACTION_VIEW,Uri.parse(AndroidUpdateFeed.REPOSITORY+"/releases")))));body.addView(Ui.button(this,"Back",false,this::finish));Ui.gap(body,18);body.addView(Ui.text(this,"Downloads use your internet connection. Android asks you to approve installation. Updates keep your encrypted vault, account settings and app approvals.",12,Ui.MUTED,false));buttons();}
    @Override protected void onResume(){super.onResume();visible=true;if(resumeInstall&&getPackageManager().canRequestPackageInstalls()){resumeInstall=false;install();}else if(resumeInstall){resumeInstall=false;status.setText("Installation permission was not granted. You can try Install update again.");}}
    @Override protected void onStop(){visible=false;updater.cancel();super.onStop();}
    @Override protected void onDestroy(){updater.cancel();worker.shutdownNow();super.onDestroy();}
    private void buttons(){check.setEnabled(!busy);download.setEnabled(!busy&&feed!=null&&!ready);install.setEnabled(!busy&&ready);}
    interface Work{void run()throws Exception;}
    private void work(Work work,Runnable done){if(busy)return;updater.cancelled.set(false);busy=true;buttons();worker.execute(()->{Exception error=null;try{work.run();}catch(Exception e){error=e;}Exception failure=error;runOnUiThread(()->{busy=false;buttons();if(!visible||isFinishing())return;progress.setVisibility(android.view.View.GONE);if(failure!=null)status.setText(failure.getMessage());else done.run();});});}
    private void check(){feed=null;ready=false;status.setText("Checking for updates…");work(()->feed=updater.check(this),()->{if(feed==null)status.setText("You're using the latest stable version.");else{long expected=updater.expectedDownload(this,feed);status.setText("Lockvyn "+feed.versionName+" available. Estimated download: "+String.format(java.util.Locale.ROOT,"%.2f",expected/1000000.0)+" MB (full APK: "+String.format(java.util.Locale.ROOT,"%.2f",feed.size/1000000.0)+" MB).");}buttons();});}
    private void download(){if(feed==null)return;status.setText("Downloading a small delta when compatible; falling back to the full signed APK if necessary…");progress.setProgress(0);progress.setVisibility(android.view.View.VISIBLE);work(()->updater.download(this,feed,p->runOnUiThread(()->{if(visible)progress.setProgress(p);})),()->{ready=true;status.setText("Update reconstructed and cryptographically verified. Tap Install update to continue.");buttons();});}
    private void install(){if(!ready||feed==null||busy)return;
        try{AndroidUpdater.verify(this,AndroidUpdater.apk(this),feed);
            if(!getPackageManager().canRequestPackageInstalls()){new AlertDialog.Builder(this).setTitle("Allow app updates").setMessage("Allow Lockvyn to open Android's installer for this app. You will still approve each update.").setNegativeButton("Cancel",null).setPositiveButton("Open Android settings",(d,w)->{resumeInstall=true;startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+getPackageName())));}).show();return;}
            Intent intent=new Intent(Intent.ACTION_INSTALL_PACKAGE,UpdateProvider.uri(this));intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);intent.putExtra(Intent.EXTRA_RETURN_RESULT,true);startActivityForResult(intent,310);
        }catch(Exception e){ready=false;buttons();status.setText(e.getMessage());}
    }
    @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);if(request==310){if(result==RESULT_OK){ready=false;feed=null;AndroidUpdater.apk(this).delete();status.setText("Update installed. Open Lockvyn again.");}else status.setText("Installation cancelled or not completed. Your vault is unchanged.");buttons();}}
}
