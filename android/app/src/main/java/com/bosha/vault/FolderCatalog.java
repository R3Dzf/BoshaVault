package com.bosha.vault;

import java.util.*;
import org.json.*;

final class FolderCatalog {
    static final String[] DEFAULTS={"Personal","Work","Email","Finance","Social","Shopping","Gaming","Other"};
    static final int MAX_FOLDERS=10008;
    static String normalize(String value)throws Exception{
        value=value.trim();if(value.isEmpty()||value.length()>100)throw new Exception("Use a folder name of 1–100 characters.");
        for(char c:value.toCharArray())if(Character.isISOControl(c))throw new Exception("Folder names cannot contain control characters.");return value;
    }
    static void add(JSONObject data,String name)throws Exception{
        name=normalize(name);JSONArray folders=data.getJSONArray("folders");for(int i=0;i<folders.length();i++)if(folders.getString(i).equalsIgnoreCase(name))return;
        if(folders.length()>=MAX_FOLDERS)throw new Exception("Folder limit reached.");folders.put(name);
    }
    static List<String> choices(JSONObject data)throws Exception{
        TreeSet<String> names=new TreeSet<>(String.CASE_INSENSITIVE_ORDER);Collections.addAll(names,DEFAULTS);
        JSONArray folders=data.optJSONArray("folders");if(folders!=null)for(int i=0;i<folders.length();i++)names.add(folders.getString(i));
        JSONArray entries=data.getJSONArray("entries");for(int i=0;i<entries.length();i++){JSONObject e=entries.getJSONObject(i);if(!e.optBoolean("purged",false)&&!e.getString("folder").isEmpty())names.add(e.getString("folder"));}return new ArrayList<>(names);
    }
    static void upgrade(JSONObject data)throws Exception{
        data.put("schema",2);if(!data.has("folders"))data.put("folders",new JSONArray());for(String name:DEFAULTS)add(data,name);
        JSONArray entries=data.getJSONArray("entries");for(int i=0;i<entries.length();i++){JSONObject e=entries.getJSONObject(i);if(!e.has("purged"))e.put("purged",false);if(!e.has("reviewAcknowledgement"))e.put("reviewAcknowledgement","");if(!e.getBoolean("purged")){String name=e.getString("folder").trim();if(name.isEmpty())name="Personal";name=normalize(name);e.put("folder",name);add(data,name);}}
    }
}
