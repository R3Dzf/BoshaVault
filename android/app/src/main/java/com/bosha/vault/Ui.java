package com.bosha.vault;

import android.app.*;
import android.content.*;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.text.InputType;
import android.view.*;
import android.view.inputmethod.EditorInfo;
import android.widget.*;

final class Ui {
    static final int INK=Color.rgb(36,35,53),MUTED=Color.rgb(121,118,142),PURPLE=Color.rgb(119,98,223),BG=Color.rgb(248,247,253),SOFT=Color.rgb(240,236,255),GREEN=Color.rgb(234,246,241);
    static int dp(Context c,int x){return Math.round(x*c.getResources().getDisplayMetrics().density);}
    static GradientDrawable shape(int fill,int radius){GradientDrawable d=new GradientDrawable();d.setColor(fill);d.setCornerRadius(radius);return d;}
    static LinearLayout column(Context c){LinearLayout x=new LinearLayout(c);x.setOrientation(LinearLayout.VERTICAL);return x;}
    static TextView text(Context c,String s,int size,int color,boolean bold){TextView t=new TextView(c);t.setText(s);t.setTextSize(size);t.setTextColor(color);if(bold)t.setTypeface(Typeface.create("sans-serif",Typeface.BOLD));return t;}
    static void gap(LinearLayout x,int h){Space s=new Space(x.getContext());x.addView(s,new LinearLayout.LayoutParams(1,dp(x.getContext(),h)));}
    static LinearLayout card(Context c,int color){LinearLayout l=column(c);int p=dp(c,18);l.setPadding(p,p,p,p);l.setBackground(shape(color,dp(c,17)));LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);lp.bottomMargin=dp(c,12);l.setLayoutParams(lp);return l;}
    static Button button(Context c,String s,boolean primary,Runnable r){Button b=new Button(c);b.setText(s);b.setTextSize(13);b.setAllCaps(false);b.setTextColor(primary?Color.WHITE:PURPLE);b.setTypeface(null,Typeface.BOLD);b.setBackground(shape(primary?PURPLE:SOFT,dp(c,12)));b.setMinHeight(dp(c,46));b.setPadding(dp(c,12),dp(c,10),dp(c,12),dp(c,10));LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);p.topMargin=dp(c,10);b.setLayoutParams(p);b.setOnClickListener(v->r.run());return b;}
    static EditText field(LinearLayout parent,String label,String value,boolean secret,int max){Context c=parent.getContext();parent.addView(text(c,label,12,MUTED,false));EditText e=new EditText(c);e.setText(value);e.setTextSize(14);e.setTextColor(INK);e.setSingleLine(true);e.setInputType(secret?InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_PASSWORD:InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);e.setImeOptions(EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING|EditorInfo.IME_ACTION_NEXT);e.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(max)});e.setBackground(shape(Color.WHITE,dp(c,10)));e.setPadding(dp(c,12),dp(c,10),dp(c,12),dp(c,10));LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,dp(c,46));p.topMargin=dp(c,7);p.bottomMargin=dp(c,15);parent.addView(e,p);return e;}
    static LinearLayout screen(Activity a){a.getWindow().setFlags(WindowManager.LayoutParams.FLAG_SECURE,WindowManager.LayoutParams.FLAG_SECURE);a.getWindow().setStatusBarColor(BG);a.getWindow().setNavigationBarColor(BG);LinearLayout root=column(a);root.setBackgroundColor(BG);root.setPadding(dp(a,23),dp(a,25),dp(a,23),dp(a,26));ScrollView s=new ScrollView(a);s.setFillViewport(true);s.addView(root);a.setContentView(s);if(Build.VERSION.SDK_INT>=35)s.setOnApplyWindowInsetsListener((v,insets)->{android.graphics.Insets sys=insets.getInsets(WindowInsets.Type.systemBars());s.setPadding(sys.left,sys.top,sys.right,sys.bottom);return insets;});return root;}
    static void error(Activity a,Exception e){new AlertDialog.Builder(a).setTitle("BoshaVault").setMessage(e.getMessage()!=null&&e.getMessage().length()<220?e.getMessage():"The action could not be completed. Your encrypted file is preserved.").setPositiveButton("OK",null).show();}
}
