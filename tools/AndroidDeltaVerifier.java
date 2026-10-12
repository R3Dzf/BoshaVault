package com.bosha.vault;

import java.io.File;

/** Synthetic fixture runner; compiled without the Android SDK on CI. */
public final class AndroidDeltaVerifier {
    public static void main(String[] args)throws Exception{
        if(args.length!=6)throw new Exception("Use base patch output base_sha target_sha target_len");
        File base=new File(args[0]), patch=new File(args[1]), result=new File(args[2]);
        String baseSha=args[3], targetSha=args[4];long targetLength=Long.parseLong(args[5]);
        AndroidDelta.apply(base,patch,result,baseSha,targetSha,targetLength,()->{});
        if(result.length()!=targetLength||!AndroidDelta.sha256(result).equalsIgnoreCase(targetSha))
            throw new Exception("Java reconstructed file differs from expected APK.");
        System.out.println("PASS Java Android delta reconstruction");
    }
}
