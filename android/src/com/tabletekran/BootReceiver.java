package com.tabletekran;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/**
 * Tablet acildiginda TabletEkran'i kullanici mudahalesi olmadan baslatir.
 * Android 3.1+ kurali: uygulama en az bir kez elle calistirilmis olmalidir.
 */
public class BootReceiver extends BroadcastReceiver {

    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent == null || !Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction())) {
            return;
        }
        Intent baslat = new Intent(context, MainActivity.class);
        baslat.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        context.startActivity(baslat);
    }
}
