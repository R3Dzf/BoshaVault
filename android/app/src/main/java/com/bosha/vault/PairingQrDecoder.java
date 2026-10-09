package com.bosha.vault;

import com.google.zxing.*;
import com.google.zxing.common.HybridBinarizer;
import java.util.*;

/** Decodes bounded in-memory luminance frames. Does not save images or access the network. */
final class PairingQrDecoder {
    static String decode(byte[] luminance,int width,int height) {
        if(width<1||height<1||width>1920||height>1920||((long)width)*height>2073600||luminance.length<(long)width*height)return null;
        Map<DecodeHintType,Object> hints=new EnumMap<>(DecodeHintType.class);
        hints.put(DecodeHintType.POSSIBLE_FORMATS,Collections.singletonList(BarcodeFormat.QR_CODE));
        hints.put(DecodeHintType.TRY_HARDER,Boolean.TRUE);
        MultiFormatReader reader=new MultiFormatReader();reader.setHints(hints);
        try {
            LuminanceSource source=new PlanarYUVLuminanceSource(luminance,width,height,0,0,width,height,false);
            String text=reader.decodeWithState(new BinaryBitmap(new HybridBinarizer(source))).getText();
            return text.length()<=4096?text:null;
        } catch(ReaderException ignored) {return null;}
        finally {reader.reset();}
    }
}
