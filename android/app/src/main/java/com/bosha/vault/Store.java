package com.bosha.vault;

import android.content.Context;
import java.io.File;
import java.nio.file.Files;
import java.util.UUID;

final class Store {
    static File vault(Context c){return new File(c.getFilesDir(),"vault.boshavault");}
    static String device(Context c)throws Exception{File f=new File(c.getFilesDir(),"device.id");if(!f.exists())Files.write(f.toPath(),UUID.randomUUID().toString().getBytes(java.nio.charset.StandardCharsets.UTF_8));String id=VaultEngine.utf8(Files.readAllBytes(f.toPath()));if(!VaultEngine.id(id))throw new Exception("Invalid device identity.");return id;}
    static VaultEngine engine(Context c)throws Exception{return new VaultEngine(vault(c),device(c));}
}
