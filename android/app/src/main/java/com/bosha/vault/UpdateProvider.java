package com.bosha.vault;

import android.content.*;
import android.database.*;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.*;

/** Grants the system installer read access to one verified cached APK only. */
public final class UpdateProvider extends ContentProvider {
    static Uri uri(Context c){return Uri.parse("content://"+c.getPackageName()+".updates/update.apk");}
    private File file(Uri uri)throws FileNotFoundException{
        if(!uri.equals(uri(getContext())))throw new FileNotFoundException("Unknown update file.");File f=AndroidUpdater.apk(getContext());if(!f.isFile())throw new FileNotFoundException("No downloaded update.");return f;
    }
    @Override public boolean onCreate(){return true;}
    @Override public String getType(Uri uri){return "application/vnd.android.package-archive";}
    @Override public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException{if(!"r".equals(mode))throw new FileNotFoundException("Update access is read only.");return ParcelFileDescriptor.open(file(uri),ParcelFileDescriptor.MODE_READ_ONLY);}
    @Override public Cursor query(Uri uri,String[] projection,String selection,String[] args,String order){try{File f=file(uri);String[] columns=projection==null?new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE}:projection;MatrixCursor result=new MatrixCursor(columns);Object[] row=new Object[columns.length];for(int i=0;i<columns.length;i++)row[i]=columns[i].equals(OpenableColumns.DISPLAY_NAME)?"Lockvyn-Android.apk":columns[i].equals(OpenableColumns.SIZE)?f.length():null;result.addRow(row);return result;}catch(FileNotFoundException e){return null;}}
    @Override public Uri insert(Uri uri,ContentValues values){throw new UnsupportedOperationException();}
    @Override public int update(Uri uri,ContentValues values,String selection,String[] args){throw new UnsupportedOperationException();}
    @Override public int delete(Uri uri,String selection,String[] args){throw new UnsupportedOperationException();}
}
