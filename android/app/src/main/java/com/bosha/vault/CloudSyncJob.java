package com.bosha.vault;

import android.app.job.*;
import android.content.*;
import java.io.File;
import java.util.Arrays;
import org.json.JSONObject;

/** Periodic encrypted snapshot exchange. A locked vault is never decrypted here. */
public final class CloudSyncJob extends JobService {
    static final int ID=4107;
    private volatile CloudClient client;
    static void schedule(Context context){
        JobScheduler jobs=(JobScheduler)context.getSystemService(Context.JOB_SCHEDULER_SERVICE);
        if(jobs==null)return;
        JobInfo info=new JobInfo.Builder(ID,new ComponentName(context,CloudSyncJob.class))
            .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).setPeriodic(15*60*1000L).build();
        jobs.schedule(info);
    }
    static void cancel(Context context){JobScheduler jobs=(JobScheduler)context.getSystemService(Context.JOB_SCHEDULER_SERVICE);if(jobs!=null)jobs.cancel(ID);}
    @Override public boolean onStartJob(JobParameters params){
        new Thread(()->{
            JSONObject profile=null;
            try{
                synchronized(CloudClient.GATE){
                    File file=Store.vault(this);if(!file.exists())return;
                    byte[] bytes=VaultEngine.read(file);String id;
                    try{id=VaultEngine.parse(bytes).getString("vaultId");}finally{Arrays.fill(bytes,(byte)0);}
                    CloudSettingsStore store=new CloudSettingsStore(this);profile=store.load(id);
                    if(profile==null||!profile.optBoolean("autoSync",true))return;
                    client=new CloudClient(()->{if(Thread.currentThread().isInterrupted())throw new InterruptedException();});
                    CloudAuth.Session signed=client.refresh(profile,store);
                    if(!client.auth.factors(signed).requiresChallenge(signed))client.syncAutomatic(null,file,profile,store,signed);
                }
            }catch(Exception ignored){/* Offline and 2FA failures retry on the next job or app unlock. */}
            finally{CloudSettingsStore.clear(profile);CloudClient current=client;client=null;if(current!=null)current.close();jobFinished(params,false);}
        },"Lockvyn encrypted cloud sync").start();return true;
    }
    @Override public boolean onStopJob(JobParameters params){CloudClient current=client;if(current!=null)current.close();return true;}
}
