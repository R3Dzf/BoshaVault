package com.bosha.vault;

import android.net.Uri;
import javax.net.ssl.HttpsURLConnection;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.URL;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import org.json.JSONArray;
import org.json.JSONObject;

final class AnnouncementClient {
    private static final int MAX_BYTES=65536;
    static final class Notice {
        final String id,title,body,severity;
        final boolean notify;
        Notice(String id,String title,String body,String severity,boolean notify){
            this.id=id;this.title=title;this.body=body;this.severity=severity;this.notify=notify;
        }
    }
    static List<Notice> fetch(int versionCode,String locale)throws Exception{
        if(versionCode<0||!(locale.equals("en")||locale.equals("ar")))
            throw new IOException("Invalid announcement feed query.");
        Uri uri=Uri.parse(CloudClient.URL+"functions/v1/lockvyn-admin/announcements")
            .buildUpon().appendQueryParameter("platform","android")
            .appendQueryParameter("version",Integer.toString(versionCode))
            .appendQueryParameter("locale",locale).build();
        HttpsURLConnection connection=(HttpsURLConnection)new URL(uri.toString()).openConnection();
        connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(8000);
        connection.setReadTimeout(10000);connection.setRequestMethod("GET");
        connection.setRequestProperty("apikey",CloudClient.PUBLIC_KEY);
        try{
            int status=connection.getResponseCode();
            if(status<200||status>=300)throw new IOException("Announcements are unavailable.");
            byte[] bytes=readBounded(connection.getInputStream());
            try{
                JSONArray array=new JSONArray(new String(bytes,java.nio.charset.StandardCharsets.UTF_8));
                if(array.length()>20)throw new IOException("Too many announcements.");
                ArrayList<Notice> result=new ArrayList<>();
                for(int i=0;i<array.length();i++){
                    JSONObject item=array.getJSONObject(i);
                    String id=item.getString("id"),title=item.getString("title"),
                        body=item.getString("body"),severity=item.getString("severity");
                    if(!UUID.fromString(id).toString().equalsIgnoreCase(id)||
                        title.trim().isEmpty()||title.length()>120||
                        body.trim().isEmpty()||body.length()>800||
                        !(severity.equals("info")||severity.equals("warning")||severity.equals("critical"))||
                        !(item.opt("notify") instanceof Boolean))
                        throw new IOException("Invalid announcement response.");
                    result.add(new Notice(id,title,body,severity,item.getBoolean("notify")));
                }
                return result;
            }finally{java.util.Arrays.fill(bytes,(byte)0);}
        }finally{connection.disconnect();}
    }
    private static byte[] readBounded(InputStream input)throws IOException{
        try(InputStream in=input;ByteArrayOutputStream out=new ByteArrayOutputStream()){
            byte[] chunk=new byte[4096];int count;
            while((count=in.read(chunk))!=-1){
                if(out.size()+count>MAX_BYTES)throw new IOException("Announcement response exceeds its size limit.");
                out.write(chunk,0,count);
            }
            return out.toByteArray();
        }
    }
}
