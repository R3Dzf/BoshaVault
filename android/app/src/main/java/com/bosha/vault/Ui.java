package com.bosha.vault;

import android.app.*;
import android.content.*;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.RippleDrawable;
import android.graphics.drawable.StateListDrawable;
import android.os.Build;
import android.text.InputType;
import android.view.*;
import android.view.inputmethod.EditorInfo;
import android.widget.*;

/**
 * Calm Pro design primitives. No data model, authentication or crypto lives here.
 * All dimensions are dp/sp, there are no forced system font scales, and touch
 * targets meet or exceed 48dp. Buttons have no native elevation/shadow.
 */
final class Ui {
    static final int INK=Color.rgb(23,27,46);
    static final int MUTED=Color.rgb(105,113,133);
    static final int PURPLE=Color.rgb(101,88,217);
    static final int BG=Color.rgb(247,248,252);
    static final int SOFT=Color.rgb(239,237,255);
    static final int GREEN=Color.rgb(234,246,241);
    static final int WHITE=Color.WHITE;
    static final int BORDER=Color.rgb(225,229,239);
    static final int SUCCESS=Color.rgb(20,134,109);

    static int dp(Context c,int x){return Math.round(x*c.getResources().getDisplayMetrics().density);}
    static ImageView brandIcon(Context c,int size){
        ImageView icon=new ImageView(c);
        icon.setImageResource(R.drawable.ic_vault);
        icon.setContentDescription("Lockvyn");
        icon.setLayoutParams(new LinearLayout.LayoutParams(dp(c,size),dp(c,size)));
        return icon;
    }
    static GradientDrawable shape(int fill,int radius){
        GradientDrawable d=new GradientDrawable();
        d.setColor(fill);d.setCornerRadius(radius);
        return d;
    }
    static GradientDrawable outlined(int fill,int radius,int border){
        GradientDrawable d=shape(fill,radius);
        d.setStroke(dpSafeStroke(),border);
        return d;
    }
    // GradientDrawable stroke needs a pixel count even without a Context.
    private static int dpSafeStroke(){return 1;}
    private static GradientDrawable outlined(Context c,int fill,int radius,int border){
        GradientDrawable d=shape(fill,dp(c,radius));
        d.setStroke(Math.max(1,dp(c,1)),border);
        return d;
    }
    private static RippleDrawable ripple(Context c,int fill,int radius,int stroke){
        GradientDrawable base=stroke==0?shape(fill,dp(c,radius)):outlined(c,fill,radius,stroke);
        return new RippleDrawable(ColorStateList.valueOf(Color.argb(45,101,88,217)),
                base,shape(Color.WHITE,dp(c,radius)));
    }
    static LinearLayout column(Context c){
        LinearLayout x=new LinearLayout(c);x.setOrientation(LinearLayout.VERTICAL);return x;
    }
    static TextView text(Context c,String s,int size,int color,boolean bold){
        TextView t=new TextView(c);t.setText(s);t.setTextSize(size);t.setTextColor(color);
        t.setFontFeatureSettings("kern");
        if(bold)t.setTypeface(Typeface.create("sans-serif-medium",Typeface.NORMAL));
        return t;
    }
    static void gap(LinearLayout x,int h){
        Space s=new Space(x.getContext());
        x.addView(s,new LinearLayout.LayoutParams(1,dp(x.getContext(),h)));
    }
    static void showKeyboard(EditText field){
        field.requestFocus();
        field.post(()->{
            if(!field.isAttachedToWindow()||!field.hasWindowFocus()||!field.hasFocus())return;
            if(Build.VERSION.SDK_INT>=30){
                WindowInsetsController c=field.getWindowInsetsController();
                if(c!=null){c.show(WindowInsets.Type.ime());return;}
            }
            android.view.inputmethod.InputMethodManager ime=(android.view.inputmethod.InputMethodManager)
                field.getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
            if(ime!=null)ime.showSoftInput(field,android.view.inputmethod.InputMethodManager.SHOW_IMPLICIT);
        });
    }
    static LinearLayout card(Context c,int color){
        LinearLayout l=column(c);
        int p=dp(c,18);l.setPadding(p,p,p,p);
        l.setBackground(outlined(c,color,18,color==WHITE?BORDER:color));
        LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2);
        lp.bottomMargin=dp(c,12);l.setLayoutParams(lp);
        return l;
    }
    static Button button(Context c,String s,boolean primary,Runnable r){
        Button b=new Button(c);
        b.setText(s);b.setTextSize(14);b.setAllCaps(false);
        b.setTextColor(primary?WHITE:PURPLE);
        b.setTypeface(Typeface.create("sans-serif-medium",Typeface.NORMAL));
        b.setBackgroundTintList(null);
        b.setStateListAnimator(null); // Android's native Button animator adds a second "shadow".
        b.setElevation(0f);
        b.setTranslationZ(0f);
        b.setBackground(ripple(c,primary?PURPLE:WHITE,13,primary?0:BORDER));
        b.setMinHeight(dp(c,50));b.setMinimumHeight(dp(c,50));
        b.setMinWidth(0);b.setMinimumWidth(0);
        b.setPadding(dp(c,16),dp(c,12),dp(c,16),dp(c,12));
        b.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);
        p.topMargin=dp(c,10);b.setLayoutParams(p);
        b.setOnClickListener(v->r.run());
        return b;
    }
    static TextView filterChip(Context c,String label,boolean selected,Runnable press){
        TextView chip=text(c,label,12,selected?PURPLE:MUTED,selected);
        chip.setGravity(Gravity.CENTER);
        chip.setMinHeight(dp(c,42));
        chip.setPadding(dp(c,5),dp(c,10),dp(c,5),dp(c,10));
        chip.setBackground(ripple(c,selected?SOFT:WHITE,12,selected?0:BORDER));
        chip.setClickable(true);chip.setFocusable(true);
        chip.setContentDescription(label+(selected?", selected":""));
        chip.setOnClickListener(v->press.run());
        return chip;
    }
    static LinearLayout bottomNavigation(Context c,String[] labels,int[] icons,int selected,Runnable[] actions){
        if(labels.length!=4||icons.length!=4||actions.length!=4)
            throw new IllegalArgumentException("Calm Pro navigation needs four destinations.");
        LinearLayout bar=new LinearLayout(c);bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setGravity(Gravity.CENTER_VERTICAL);bar.setPadding(dp(c,2),dp(c,8),dp(c,2),dp(c,4));
        bar.setBackground(outlined(c,WHITE,18,BORDER));
        bar.setElevation(0f);
        for(int i=0;i<4;i++){
            final int item=i;boolean active=i==selected;
            LinearLayout slot=column(c);slot.setGravity(Gravity.CENTER);
            slot.setMinimumHeight(dp(c,58));
            slot.setPadding(0,dp(c,5),0,dp(c,5));
            slot.setBackground(ripple(c,WHITE,12,0));
            slot.setClickable(true);slot.setFocusable(true);
            slot.setContentDescription(labels[i]+(active?", selected":""));
            ImageView icon=new ImageView(c);
            icon.setImageResource(icons[i]);icon.setImageTintList(ColorStateList.valueOf(active?PURPLE:MUTED));
            LinearLayout.LayoutParams image=new LinearLayout.LayoutParams(dp(c,22),dp(c,22));
            image.gravity=Gravity.CENTER;slot.addView(icon,image);
            TextView caption=text(c,labels[i],11,active?PURPLE:MUTED,active);
            caption.setGravity(Gravity.CENTER);caption.setSingleLine(true);
            LinearLayout.LayoutParams cap=new LinearLayout.LayoutParams(-1,-2);cap.topMargin=dp(c,5);
            slot.addView(caption,cap);
            slot.setOnClickListener(v->actions[item].run());
            bar.addView(slot,new LinearLayout.LayoutParams(0,-2,1));
        }
        return bar;
    }
    private static GradientDrawable fieldShape(Context c,int fill,int border,int width){
        GradientDrawable background=shape(fill,dp(c,12));
        background.setStroke(dp(c,width),border);return background;
    }
    static EditText field(LinearLayout parent,String label,String value,boolean secret,int max){
        Context c=parent.getContext();
        TextView caption=text(c,label,13,INK,true);parent.addView(caption);
        EditText e=new EditText(c);e.setId(View.generateViewId());e.setSaveEnabled(false);
        caption.setLabelFor(e.getId());
        e.setText(value);e.setTextSize(16);e.setTextColor(INK);
        e.setHintTextColor(MUTED);e.setSingleLine(true);
        e.setInputType(secret?InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_PASSWORD:
            InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
        e.setImeOptions(EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING|EditorInfo.IME_ACTION_NEXT);
        e.setFilters(new android.text.InputFilter[]{new android.text.InputFilter.LengthFilter(max)});
        StateListDrawable background=new StateListDrawable();
        background.addState(new int[]{android.R.attr.state_enabled,android.R.attr.state_focused},
            fieldShape(c,WHITE,PURPLE,2));
        background.addState(new int[]{-android.R.attr.state_enabled},
            fieldShape(c,BG,BORDER,1));
        background.addState(new int[]{},fieldShape(c,WHITE,BORDER,1));
        e.setBackgroundTintList(null);e.setBackground(background);
        e.setMinHeight(dp(c,56));
        e.setPadding(dp(c,16),dp(c,14),dp(c,16),dp(c,14));
        LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,-2);
        p.topMargin=dp(c,7);p.bottomMargin=dp(c,16);
        e.setShowSoftInputOnFocus(true);e.setOnClickListener(v->showKeyboard(e));
        parent.addView(e,p);return e;
    }
    static LinearLayout screen(Activity a){
        a.getWindow().setFlags(WindowManager.LayoutParams.FLAG_SECURE,WindowManager.LayoutParams.FLAG_SECURE);
        a.getWindow().setStatusBarColor(BG);a.getWindow().setNavigationBarColor(BG);
        LinearLayout root=column(a);root.setBackgroundColor(BG);
        root.setPadding(dp(a,20),dp(a,24),dp(a,20),dp(a,24));
        ScrollView s=new ScrollView(a);s.setFillViewport(true);s.addView(root);a.setContentView(s);
        if(Build.VERSION.SDK_INT>=35)s.setOnApplyWindowInsetsListener((v,insets)->{
            android.graphics.Insets sys=insets.getInsets(WindowInsets.Type.systemBars());
            s.setPadding(sys.left,sys.top,sys.right,sys.bottom);return insets;
        });
        return root;
    }
    static LinearLayout fixedScreen(Activity a){
        a.getWindow().setFlags(WindowManager.LayoutParams.FLAG_SECURE,WindowManager.LayoutParams.FLAG_SECURE);
        a.getWindow().setStatusBarColor(BG);a.getWindow().setNavigationBarColor(BG);
        LinearLayout root=column(a);root.setBackgroundColor(BG);
        int horizontal=dp(a,20),vertical=dp(a,14);
        root.setPadding(horizontal,vertical,horizontal,vertical);
        root.setFocusableInTouchMode(true);a.setContentView(root);root.requestFocus();
        root.post(()->{
            android.view.inputmethod.InputMethodManager ime=(android.view.inputmethod.InputMethodManager)
                a.getSystemService(Context.INPUT_METHOD_SERVICE);
            if(ime!=null)ime.hideSoftInputFromWindow(root.getWindowToken(),0);
        });
        if(Build.VERSION.SDK_INT>=35)root.setOnApplyWindowInsetsListener((v,insets)->{
            android.graphics.Insets sys=insets.getInsets(WindowInsets.Type.systemBars());
            root.setPadding(horizontal+sys.left,vertical+sys.top,horizontal+sys.right,vertical+sys.bottom);
            return insets;
        });
        return root;
    }
    static void error(Activity a,Exception e){
        String type=e.getClass().getSimpleName();
        String message=e.getMessage();Throwable cause=e.getCause();
        if((message==null||message.trim().isEmpty())&&cause!=null){
            type=type+" / "+cause.getClass().getSimpleName();message=cause.getMessage();
        }
        if(message==null||message.trim().isEmpty())message="Android supplied no further error details.";
        message=message.replace('\r',' ').replace('\n',' ').trim();
        if(message.length()>360)message=message.substring(0,357)+"...";
        new AlertDialog.Builder(a).setTitle("Lockvyn · "+type)
            .setMessage(message+"\n\nYour encrypted vault was not deleted. Unlock with your master passphrase.")
            .setPositiveButton("OK",null).show();
    }
}
