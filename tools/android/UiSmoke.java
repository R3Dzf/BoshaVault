package com.bosha.vault.tests;

import android.app.*;
import android.content.*;
import android.graphics.Rect;
import android.os.*;
import android.view.*;
import android.widget.*;
import android.accessibilityservice.AccessibilityServiceInfo;
import android.view.accessibility.AccessibilityWindowInfo;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicReference;
import org.json.*;

/** Disposable emulator-only acceptance tests. Never submits signup or uploads data. */
public final class UiSmoke extends Instrumentation {
    private Activity activity;
    private int passed;
    private interface Work { void run() throws Exception; }
    private void check(boolean good,String name)throws Exception{if(!good)throw new Exception("FAIL "+name);passed++;}
    private void main(Work work)throws Exception{
        AtomicReference<Exception> failure=new AtomicReference<>();runOnMainSync(()->{try{work.run();}catch(Exception e){failure.set(e);}});if(failure.get()!=null)throw failure.get();
    }
    private Object field(String name)throws Exception{Field f=activity.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(activity);}
    private void invoke(String name)throws Exception{Method m=activity.getClass().getDeclaredMethod(name);m.setAccessible(true);m.invoke(activity);}
    private static <T extends View> T find(View v,Class<T> type,String text){
        if(type.isInstance(v)&&(text==null||(v instanceof TextView&&((TextView)v).getText().toString().equals(text))))return type.cast(v);
        if(v instanceof ViewGroup){ViewGroup group=(ViewGroup)v;for(int i=0;i<group.getChildCount();i++){T match=find(group.getChildAt(i),type,text);if(match!=null)return match;}}return null;
    }
    @SuppressWarnings("unchecked") private AlertDialog currentDialog()throws Exception{List<AlertDialog> all=(List<AlertDialog>)field("dialogs");return all.get(all.size()-1);}
    private Rect visible(View view)throws Exception{Rect r=new Rect();check(view!=null&&view.getGlobalVisibleRect(r)&&r.height()>0,"view is visible");return r;}
    private boolean keyboardVisible(){
        // Check the interactive keyboard window on the screen, including floating dialogs.
        for(AccessibilityWindowInfo window:getUiAutomation().getWindows()){Rect bounds=new Rect();window.getBoundsInScreen(bounds);if(window.getType()==AccessibilityWindowInfo.TYPE_INPUT_METHOD&&!bounds.isEmpty())return true;}return false;
    }
    private void tap(EditText field)throws Exception{
        main(()->{field.requestFocus();field.requestRectangleOnScreen(new Rect(0,0,field.getWidth(),field.getHeight()),true);});waitForIdleSync();Rect r=new Rect();
        // Pointer injection uses screen coordinates; a dialog's root is offset on screen.
        main(()->{if(!field.getLocalVisibleRect(r))throw new Exception("Editor is off screen.");int[] location=new int[2];field.getLocationOnScreen(location);r.offset(location[0],location[1]);});long time=SystemClock.uptimeMillis();
        MotionEvent down=MotionEvent.obtain(time,time,MotionEvent.ACTION_DOWN,r.centerX(),r.centerY(),0),up=MotionEvent.obtain(time,time+40,MotionEvent.ACTION_UP,r.centerX(),r.centerY(),0);down.setSource(InputDevice.SOURCE_TOUCHSCREEN);up.setSource(InputDevice.SOURCE_TOUCHSCREEN);sendPointerSync(down);sendPointerSync(up);down.recycle();up.recycle();waitForIdleSync();
    }
    @Override public void onCreate(Bundle arguments){super.onCreate(arguments);start();}
    @Override public void onStart(){
        Bundle result=new Bundle();int resultCode=Activity.RESULT_CANCELED;
        try{
            Context target=getTargetContext();if(!target.getPackageName().equals("com.lockvyn.app"))throw new Exception("UI smoke requires the isolated Lockvyn package.");
            AccessibilityServiceInfo service=getUiAutomation().getServiceInfo();service.flags|=AccessibilityServiceInfo.FLAG_RETRIEVE_INTERACTIVE_WINDOWS;getUiAutomation().setServiceInfo(service);
            activity=startActivitySync(new Intent().setComponent(new ComponentName(target.getPackageName(),"com.bosha.vault.MainActivity")).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));waitForIdleSync();
            Object engine=field("engine");Class<?> type=engine.getClass();Method create=type.getDeclaredMethod("create",String.class);create.setAccessible(true);
            main(()->{Field touch=activity.getClass().getDeclaredField("lastTouch");touch.setAccessible(true);touch.setLong(activity,SystemClock.elapsedRealtime());});
            // create() refuses to overwrite an existing vault; this test never deletes one.
            create.invoke(engine,"synthetic emulator master passphrase 2026");Method snapshot=type.getDeclaredMethod("snapshot");snapshot.setAccessible(true);JSONObject data=(JSONObject)snapshot.invoke(engine);
            Method blank=type.getDeclaredMethod("blank");blank.setAccessible(true);JSONArray entries=new JSONArray();String device=UUID.randomUUID().toString();
            for(int i=0;i<1000;i++){JSONObject e=(JSONObject)blank.invoke(null);e.put("title",String.format(Locale.ROOT,"Synthetic login %04d",i)).put("username","demo@example.invalid").put("password","Synthetic random secret 42!").put("clock",new JSONObject().put(device,1));entries.put(e);}data.put("entries",entries);Field body=type.getDeclaredField("data");body.setAccessible(true);body.set(engine,data);
            main(()->invoke("showVault"));waitForIdleSync();final Rect[] before={null};
            main(()->{ListView list=(ListView)field("list");check(list.getAdapter().getCount()==1000,"all 1000 logins are listed");check(list.getChildCount()>0&&list.getChildCount()<40,"rows are recycled instead of creating 1000 views");before[0]=visible(find(activity.findViewById(android.R.id.content),Button.class,"Options"));list.setSelection(999);});waitForIdleSync();
            main(()->{Rect after=visible(find(activity.findViewById(android.R.id.content),Button.class,"Options"));check(before[0].equals(after),"Options stays fixed after scrolling to login 1000");invoke("cloud");});waitForIdleSync();
            final AlertDialog[] dialog={null};final EditText[] email={null},password={null},confirmation={null};
            main(()->{dialog[0]=currentDialog();Window window=dialog[0].getWindow();int flags=window.getAttributes().flags;check((flags&WindowManager.LayoutParams.FLAG_ALT_FOCUSABLE_IM)==0,"cloud dialog allows IME connection");check((flags&WindowManager.LayoutParams.FLAG_SECURE)!=0,"cloud dialog retains screenshot protection");List<EditText> fields=new ArrayList<>();collect(window.getDecorView(),fields);check(fields.size()==4,"cloud email confirmation editor is present");email[0]=fields.get(0);password[0]=fields.get(1);confirmation[0]=fields.get(3);check(find(window.getDecorView(),Button.class,"Confirm email code")!=null,"email confirmation action is available");});
            tap(email[0]);boolean keyboard=false;
            for(int i=0;i<40&&!keyboard;i++){keyboard=keyboardVisible();if(!keyboard)SystemClock.sleep(100);}
            if(!keyboard){String[] state={""};main(()->{android.view.inputmethod.InputMethodManager ime=(android.view.inputmethod.InputMethodManager)target.getSystemService(Context.INPUT_METHOD_SERVICE);state[0]=" (fieldFocus="+email[0].hasFocus()+", windowFocus="+email[0].hasWindowFocus()+", editorConnected="+(ime!=null&&ime.isActive(email[0]))+", flags="+Integer.toHexString(dialog[0].getWindow().getAttributes().flags)+")";});throw new Exception("FAIL soft keyboard appears on cloud email field"+state[0]);}
            check(keyboard,"soft keyboard appears on cloud email field");sendStringSync("demo@example.invalid");waitForIdleSync();main(()->check(email[0].getText().toString().equals("demo@example.invalid"),"email field accepts keyboard input"));
            tap(password[0]);sendStringSync("synthetic-password-123");waitForIdleSync();main(()->{check(password[0].getText().toString().equals("synthetic-password-123"),"cloud password accepts keyboard input");check((password[0].getInputType()&android.text.InputType.TYPE_TEXT_VARIATION_PASSWORD)!=0,"cloud password stays masked");});
            tap(confirmation[0]);sendStringSync("123456");waitForIdleSync();main(()->{check(confirmation[0].getText().toString().equals("123456"),"email confirmation accepts numeric keyboard input");dialog[0].dismiss();check(confirmation[0].getText().length()==0,"email code cleared on dismissal");});
            // Render MFA screens with fake models and no CloudClient. No request action is pressed.
            ClassLoader loader=activity.getClassLoader();Class<?> sessionType=loader.loadClass("com.bosha.vault.CloudAuth$Session"),factorType=loader.loadClass("com.bosha.vault.CloudAuth$Factor"),enrollmentType=loader.loadClass("com.bosha.vault.CloudAuth$Enrollment");
            String owner=UUID.randomUUID().toString();String jwt="eyJhbGciOiJIUzI1NiJ9."+android.util.Base64.encodeToString(new JSONObject().put("sub",owner).put("aal","aal1").toString().getBytes("UTF-8"),android.util.Base64.URL_SAFE|android.util.Base64.NO_PADDING|android.util.Base64.NO_WRAP)+".synthetic-signature";
            Constructor<?> sessionCtor=sessionType.getDeclaredConstructor(byte[].class);sessionCtor.setAccessible(true);Object session=sessionCtor.newInstance((Object)new JSONObject().put("user",new JSONObject().put("id",owner)).put("access_token",jwt).put("refresh_token","synthetic-refresh-token").toString().getBytes("UTF-8"));
            Constructor<?> factorCtor=factorType.getDeclaredConstructor(JSONObject.class);factorCtor.setAccessible(true);Object factor=factorCtor.newInstance(new JSONObject().put("id",UUID.randomUUID().toString()).put("factor_type","totp").put("status","verified").put("friendly_name","Synthetic authenticator"));
            Method verify=Arrays.stream(activity.getClass().getDeclaredMethods()).filter(m->m.getName().equals("cloudVerify")).findFirst().get();verify.setAccessible(true);
            Class<?> readyType=verify.getParameterTypes()[5];Object ready=java.lang.reflect.Proxy.newProxyInstance(loader,new Class<?>[]{readyType},(proxy,method,args)->{throw new IllegalStateException("No verification or cloud request is allowed in UI smoke");});
            final EditText[] otp={null};main(()->{verify.invoke(activity,null,session,null,null,Collections.singletonList(factor),ready);dialog[0]=currentDialog();Window w=dialog[0].getWindow();check((w.getAttributes().flags&WindowManager.LayoutParams.FLAG_SECURE)!=0,"MFA dialog prevents screenshots");check((w.getAttributes().flags&WindowManager.LayoutParams.FLAG_ALT_FOCUSABLE_IM)==0,"MFA dialog permits keyboard");List<EditText> fields=new ArrayList<>();collect(w.getDecorView(),fields);check(fields.size()==1,"MFA code editor is present");otp[0]=fields.get(0);check(otp[0].getTransformationMethod() instanceof android.text.method.PasswordTransformationMethod,"MFA code stays masked");});
            tap(otp[0]);sendStringSync("1234567");waitForIdleSync();main(()->{check(otp[0].getText().toString().equals("123456"),"MFA code keyboard entry is bounded to six digits");find(dialog[0].getWindow().getDecorView(),Button.class,"Cancel").performClick();check(otp[0].getText().length()==0,"MFA cancel clears entered code");});
            Constructor<?> enrollCtor=enrollmentType.getDeclaredConstructor(String.class,String.class,String.class);enrollCtor.setAccessible(true);Object enrollment=enrollCtor.newInstance(UUID.randomUUID().toString(),"JBSWY3DPEHPK3PXP","otpauth://totp/Lockvyn%3Afake%40example.invalid?secret=JBSWY3DPEHPK3PXP&issuer=Lockvyn&algorithm=SHA1&digits=6&period=30");
            Method enroll=Arrays.stream(activity.getClass().getDeclaredMethods()).filter(m->m.getName().equals("cloudEnrollment")).findFirst().get();enroll.setAccessible(true);
            main(()->{enroll.invoke(activity,null,session,enrollment,new JSONObject().put("email","fake@example.invalid"),null,(Runnable)()->{});dialog[0]=currentDialog();Window w=dialog[0].getWindow();check((w.getAttributes().flags&WindowManager.LayoutParams.FLAG_SECURE)!=0,"MFA setup protects QR and key from screenshots");ImageView qr=find(w.getDecorView(),ImageView.class,null);check(qr!=null&&qr.getDrawable()!=null,"authenticator QR rendered locally");List<EditText> fields=new ArrayList<>();collect(w.getDecorView(),fields);check(fields.size()==2&&fields.get(0).getText().toString().equals("JBSWY3DPEHPK3PXP"),"manual authenticator key is available");dialog[0].dismiss();check(fields.get(0).getText().length()==0,"authenticator key cleared on close");Field secret=enrollmentType.getDeclaredField("secret");secret.setAccessible(true);check(secret.get(enrollment).equals(""),"closed setup drops secret model reference");});
            result.putString("stream","UI_SMOKE_PASS: "+passed+" native Android keyboard and 1000-row navigation checks passed. No email or cloud request was submitted.\n");resultCode=Activity.RESULT_OK;
        }catch(Exception e){result.putString("stream","UI_SMOKE_FAIL: "+e.toString()+"\n");}
        finally{if(activity!=null)runOnMainSync(()->activity.finish());}
        finish(resultCode,result);
    }
    private static void collect(View view,List<EditText> out){if(view instanceof EditText)out.add((EditText)view);if(view instanceof ViewGroup){ViewGroup group=(ViewGroup)view;for(int i=0;i<group.getChildCount();i++)collect(group.getChildAt(i),out);}}
}
