using BoshaVault.Core;
using BoshaVault.Windows;

public static class CloudStoreTests
{
    public static void Run()
    {
        if(!OperatingSystem.IsWindows())throw new Exception("DPAPI test requires Windows.");
        string root=Path.Combine(Path.GetTempPath(),"bv-cloud-dpapi-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            var profile=new CloudProfile{OwnerId=Guid.NewGuid(),VaultId=Guid.NewGuid(),VaultSlot=Guid.NewGuid(),DeviceSlot=Guid.NewGuid(),Email="synthetic@example.invalid",RefreshToken="synthetic-private-refresh-token-not-real",RecoveryKey=CloudSnapshotCodec.NewRecoveryKey()};
            var store=new CloudProfileStore(root);store.Save(profile);
            string raw=System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"cloud-profile.dpapi")));
            if(raw.Contains(profile.RefreshToken)||raw.Contains(profile.RecoveryKey)||raw.Contains(profile.Email))throw new Exception("DPAPI file exposed cloud secrets.");
            var loaded=store.Load(profile.VaultId)!;if(loaded.RefreshToken!=profile.RefreshToken||loaded.RecoveryKey!=profile.RecoveryKey)throw new Exception("DPAPI cloud profile roundtrip failed.");
            byte[] bytes=File.ReadAllBytes(Path.Combine(root,"cloud-profile.dpapi"));bytes[^1]^=1;File.WriteAllBytes(Path.Combine(root,"cloud-profile.dpapi"),bytes);
            bool rejected=false;try{store.Load(profile.VaultId);}catch(VaultException){rejected=true;}if(!rejected)throw new Exception("DPAPI accepted tampered profile.");
            store.Forget();if(store.Exists)throw new Exception("Cloud settings were not forgotten.");profile.Clear();loaded.Clear();
            Console.WriteLine("RESULT: 4 Windows DPAPI storage checks passed (opaque disk content, roundtrip, tamper rejection, removal).");
        }
        finally{Directory.Delete(root,true);}
    }
}
