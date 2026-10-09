package com.bosha.vault;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.ImageFormat;
import android.hardware.Camera;
import android.os.*;
import android.view.*;
import android.widget.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Explicit, offline camera capture for the Windows pairing QR. No photos are written. */
@SuppressWarnings("deprecation")
public final class ScanQrActivity extends Activity implements SurfaceHolder.Callback {
    static final String RESULT_PAIRING="com.bosha.vault.PAIRING_CODE";
    private final ExecutorService decoder=Executors.newSingleThreadExecutor();
    private final AtomicBoolean decoding=new AtomicBoolean();
    private SurfaceView preview; private FrameLayout previewBox; private TextView status; private Button permissionButton;
    private Camera camera; private boolean resumed,completed; private int generation; private long lastFrame;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);setResult(RESULT_CANCELED);
        LinearLayout root=Ui.screen(this);
        root.addView(Ui.text(this,"Scan your Windows QR",25,Ui.INK,true));Ui.gap(root,12);
        root.addView(Ui.text(this,"Start a private transfer on your computer. Point the camera at the QR shown in BoshaVault.",14,Ui.MUTED,false));Ui.gap(root,18);
        previewBox=new FrameLayout(this);previewBox.setBackgroundColor(Ui.INK);
        preview=new SurfaceView(this);preview.getHolder().addCallback(this);
        previewBox.addView(preview,new FrameLayout.LayoutParams(-1,-1,Gravity.CENTER));
        root.addView(previewBox,new LinearLayout.LayoutParams(-1,Ui.dp(this,350)));Ui.gap(root,16);
        status=Ui.text(this,"Camera access is used only while scanning. Images stay on this phone and are not saved.",13,Ui.MUTED,false);root.addView(status);
        permissionButton=Ui.button(this,"Allow camera & scan",true,()->{if(checkSelfPermission(Manifest.permission.CAMERA)==PackageManager.PERMISSION_GRANTED)openCamera();else requestPermissions(new String[]{Manifest.permission.CAMERA},1);});root.addView(permissionButton);
        root.addView(Ui.button(this,"Cancel — paste code instead",false,this::finish));
        if(checkSelfPermission(Manifest.permission.CAMERA)!=PackageManager.PERMISSION_GRANTED&&state==null)requestPermissions(new String[]{Manifest.permission.CAMERA},1);
    }
    @Override protected void onResume(){super.onResume();resumed=true;openCamera();}
    @Override protected void onPause(){resumed=false;generation++;closeCamera();super.onPause();}
    @Override protected void onDestroy(){closeCamera();decoder.shutdownNow();super.onDestroy();}
    @Override public void surfaceCreated(SurfaceHolder holder){openCamera();}
    @Override public void surfaceChanged(SurfaceHolder holder,int format,int width,int height){openCamera();}
    @Override public void surfaceDestroyed(SurfaceHolder holder){generation++;closeCamera();}
    @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] grants){
        super.onRequestPermissionsResult(request,permissions,grants);
        if(request==1&&grants.length>0&&grants[0]==PackageManager.PERMISSION_GRANTED)openCamera();
        else status.setText("Camera access was not allowed. You can close this screen and paste the pairing code instead.");
    }
    private void openCamera(){
        if(!resumed||completed||camera!=null||!preview.getHolder().getSurface().isValid()||checkSelfPermission(Manifest.permission.CAMERA)!=PackageManager.PERMISSION_GRANTED)return;
        try {
            int id=-1;Camera.CameraInfo info=new Camera.CameraInfo();
            for(int i=0;i<Camera.getNumberOfCameras();i++){Camera.getCameraInfo(i,info);if(info.facing==Camera.CameraInfo.CAMERA_FACING_BACK){id=i;break;}}
            if(id<0)throw new Exception("No rear camera is available.");
            Camera.CameraInfo chosen=new Camera.CameraInfo();Camera.getCameraInfo(id,chosen);
            camera=Camera.open(id);camera.setErrorCallback((error,c)->{generation++;closeCamera();status.setText("The camera stopped. Tap Allow camera & scan to retry, or paste the code instead.");permissionButton.setVisibility(View.VISIBLE);});
            Camera.Parameters params=camera.getParameters();Camera.Size size=chooseSize(params.getSupportedPreviewSizes());
            if(size==null)throw new Exception("No supported camera preview size is available.");
            if(!params.getSupportedPreviewFormats().contains(ImageFormat.NV21))throw new Exception("This camera cannot provide a QR preview.");
            params.setPreviewFormat(ImageFormat.NV21);params.setPreviewSize(size.width,size.height);
            List<String> modes=params.getSupportedFocusModes();
            if(modes!=null&&modes.contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE))params.setFocusMode(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE);
            else if(modes!=null&&modes.contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_VIDEO))params.setFocusMode(Camera.Parameters.FOCUS_MODE_CONTINUOUS_VIDEO);
            camera.setParameters(params);
            int rotation=getWindowManager().getDefaultDisplay().getRotation();
            int degrees=rotation==Surface.ROTATION_90?90:rotation==Surface.ROTATION_180?180:rotation==Surface.ROTATION_270?270:0;
            int angle=(chosen.orientation-degrees+360)%360;camera.setDisplayOrientation(angle);
            fitPreview(size.width,size.height,angle);
            camera.setPreviewDisplay(preview.getHolder());
            int bytes=size.width*size.height*ImageFormat.getBitsPerPixel(ImageFormat.NV21)/8;
            camera.addCallbackBuffer(new byte[bytes]);camera.addCallbackBuffer(new byte[bytes]);
            final int width=size.width,height=size.height,stamp=++generation;
            camera.setPreviewCallbackWithBuffer((frame,source)->onFrame(frame,source,width,height,stamp));
            camera.startPreview();permissionButton.setVisibility(View.GONE);
            status.setText("Keep the whole QR in view. Only BoshaVault pairing codes are accepted.");
            if(modes!=null&&Camera.Parameters.FOCUS_MODE_AUTO.equals(params.getFocusMode()))preview.setOnClickListener(v->{try{if(camera!=null)camera.autoFocus(null);}catch(RuntimeException ignored){}});
        }catch(Exception e){closeCamera();permissionButton.setVisibility(View.VISIBLE);status.setText("Cannot open the camera. Close other camera apps and retry, or paste the pairing code instead.");}
    }
    private static Camera.Size chooseSize(List<Camera.Size> sizes){
        Camera.Size best=null;long bestDistance=Long.MAX_VALUE;
        for(Camera.Size size:sizes){long area=(long)size.width*size.height;if(size.width<480||size.height<320||size.width>1920||size.height>1920||area>2073600)continue;long distance=Math.abs(area-921600);if(distance<bestDistance){best=size;bestDistance=distance;}}
        return best;
    }
    private void fitPreview(int width,int height,int angle){
        int boxWidth=previewBox.getWidth(),boxHeight=previewBox.getHeight();if(boxWidth<1||boxHeight<1)return;
        double ratio=angle==90||angle==270?height/(double)width:width/(double)height;
        int w=Math.min(boxWidth,(int)(boxHeight*ratio)),h=(int)(w/ratio);
        preview.setLayoutParams(new FrameLayout.LayoutParams(w,h,Gravity.CENTER));
    }
    private void onFrame(byte[] frame,Camera source,int width,int height,int stamp){
        byte[] luminance=null;long now=SystemClock.elapsedRealtime();
        if(resumed&&!completed&&frame!=null&&frame.length>=width*height&&now-lastFrame>=180&&decoding.compareAndSet(false,true)){lastFrame=now;luminance=Arrays.copyOf(frame,width*height);}
        try{source.addCallbackBuffer(frame);}catch(RuntimeException ignored){}
        if(luminance==null)return;final byte[] pixels=luminance;
        try{decoder.execute(()->{
            String result=null;
            try{result=PairingQrDecoder.decode(pixels,width,height);}finally{Arrays.fill(pixels,(byte)0);decoding.set(false);}
            if(result==null)return;final String code=result;
            runOnUiThread(()->{
                if(!resumed||completed||stamp!=generation)return;
                try{new LocalTransfer(code);}catch(Exception ignored){status.setText("This is not a valid BoshaVault pairing QR. Scan the QR in the Windows transfer window.");return;}
                completed=true;generation++;closeCamera();setResult(RESULT_OK,new Intent().putExtra(RESULT_PAIRING,code));finish();
            });
        });}catch(RejectedExecutionException ignored){Arrays.fill(pixels,(byte)0);decoding.set(false);}
    }
    private void closeCamera(){
        Camera current=camera;camera=null;
        if(current!=null){try{current.setPreviewCallbackWithBuffer(null);current.stopPreview();}catch(RuntimeException ignored){}finally{current.release();}}
    }
}
