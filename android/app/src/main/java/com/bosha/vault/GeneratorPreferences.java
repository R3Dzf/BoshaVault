package com.bosha.vault;

import android.content.*;

final class GeneratorPreferences {
    static PasswordOptions load(Context context){
        SharedPreferences p=context.getSharedPreferences("password_generator",Context.MODE_PRIVATE);PasswordOptions o=new PasswordOptions();
        try{o.length=p.getInt("length",24);o.lowercase=p.getBoolean("lowercase",true);o.uppercase=p.getBoolean("uppercase",true);o.digits=p.getBoolean("digits",true);o.symbols=p.getBoolean("symbols",true);o.excludeAmbiguous=p.getBoolean("excludeAmbiguous",true);o.allowedSymbols=p.getString("allowedSymbols",PasswordOptions.DEFAULT_SYMBOLS);o.minimumDigits=p.getInt("minimumDigits",1);o.minimumSymbols=p.getInt("minimumSymbols",1);o.validate();return o;}catch(Exception e){return new PasswordOptions();}
    }
    static void save(Context context,PasswordOptions o)throws Exception{
        o.validate();context.getSharedPreferences("password_generator",Context.MODE_PRIVATE).edit().putInt("length",o.length).putBoolean("lowercase",o.lowercase).putBoolean("uppercase",o.uppercase).putBoolean("digits",o.digits).putBoolean("symbols",o.symbols).putBoolean("excludeAmbiguous",o.excludeAmbiguous).putString("allowedSymbols",o.allowedSymbols).putInt("minimumDigits",o.minimumDigits).putInt("minimumSymbols",o.minimumSymbols).apply();
    }
}
