package com.bosha.vault;

import android.content.*;

public final class CloudBootReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context context,Intent intent){
        if(Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction()))CloudSyncJob.schedule(context);
    }
}
