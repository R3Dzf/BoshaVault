using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoshaVault.Core;

/// <summary>Opt-in live MFA acceptance against one disposable, confirmed synthetic user. Sends no email.</summary>
public static class CloudMfaLiveTests
{
    private static string Code(string base32)
    {
        const string alphabet="ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";var secret=new List<byte>();int bits=0,value=0;
        foreach(char c in base32){value=(value<<5)|alphabet.IndexOf(c);bits+=5;if(bits>=8){bits-=8;secret.Add((byte)(value>>bits));}}
        byte[] key=secret.ToArray(),counter=BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds()/30);if(BitConverter.IsLittleEndian)Array.Reverse(counter);
        byte[] digest=HMACSHA1.HashData(key,counter);int offset=digest[^1]&15;
        try{return (((digest[offset]&127)<<24|digest[offset+1]<<16|digest[offset+2]<<8|digest[offset+3])%1000000).ToString("D6");}
        finally{CryptographicOperations.ZeroMemory(key);CryptographicOperations.ZeroMemory(digest);secret.Clear();}
    }
    public static async Task Run(string configuration)
    {
        using var json=JsonDocument.Parse(File.ReadAllBytes(configuration));var c=json.RootElement;
        using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};var auth=new SupabaseCloudAccount(http);
        string email=c.GetProperty("emailA").GetString()!,password=c.GetProperty("passwordA").GetString()!;Guid owner=c.GetProperty("idA").GetGuid();
        var session=await auth.SignIn(email,password);int passed=0;
        void Check(bool good,string name){if(!good)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);passed++;}
        async Task Refuse(Func<Task> run,string name){bool rejected=false;try{await run();}catch(VaultException){rejected=true;}Check(rejected,name);}
        string folder=Path.Combine(Path.GetTempPath(),"lockvyn-mfa-live-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        byte[] key=CloudSnapshotCodec.ParseRecoveryKey(CloudSnapshotCodec.NewRecoveryKey());
        try
        {
            Check(session.OwnerId==owner&&!session.HasSecondFactor,"live password login starts at AAL1");
            using var vault=await VaultSession.Create(folder+"/vault","synthetic MFA acceptance master passphrase",Guid.NewGuid().ToString());
            vault.Upsert(new VaultEntry{Title="Synthetic MFA acceptance",Password="OnlySyntheticData-234!"});Guid slot=Guid.NewGuid(),device=Guid.NewGuid();
            byte[] encrypted=CloudSnapshotCodec.Seal(vault.ExportEncrypted(),key,slot,device);
            SupabaseSnapshotTransport Transport(SupabaseCloudAccount.Session s)=>new(http,new Uri(CloudProject.Url),CloudProject.PublishableKey,_=>Task.FromResult(s.AccessToken));
            var weak=session;await Transport(weak).UploadAsync(owner,slot,device,encrypted);Check((await Transport(weak).ListAsync(slot)).Count==1,"live optional account can sync before enabling MFA");
            using var enrollment=await auth.EnrollTotp(session,email);
            var factors=await auth.ListFactors(session);Check(!factors.HasVerified&&factors.All.Any(f=>f.Id==enrollment.Id),"live enrollment stays pending until a valid code");
            Check((await Transport(weak).ListAsync(slot)).Count==1,"live pending setup preserves current cloud access");
            string incorrect=Code(enrollment.Secret)=="000000"?"111111":"000000";
            await Refuse(()=>auth.VerifyTotp(session,enrollment.Id,incorrect),"live wrong TOTP cannot activate MFA");
            session=await auth.VerifyTotp(session,enrollment.Id,Code(enrollment.Secret));Check(session.HasSecondFactor&&session.OwnerId==owner,"live TOTP enrollment returns AAL2 for the same owner");
            factors=await auth.ListFactors(session);Check(factors.HasVerified&&factors.Totp.Count==1,"live verified authenticator is listed");
            Check((await Transport(weak).ListAsync(slot)).Count==0,"live old AAL1 JWT cannot read after MFA activation");
            await Refuse(()=>Transport(weak).UploadAsync(owner,slot,Guid.NewGuid(),encrypted),"live AAL1 cannot write encrypted backups after MFA activation");
            Check((await Transport(session).ListAsync(slot)).Count==1,"live AAL2 session can read its encrypted backup");
            var passwordOnly=await auth.SignIn(email,password);Check((await auth.ListFactors(passwordOnly)).RequiresChallenge(passwordOnly),"live fresh password login requires an authenticator challenge");
            await Refuse(()=>auth.Unenroll(passwordOnly,enrollment.Id),"live password-only session cannot disable MFA");
            session=await auth.Refresh(session.RefreshToken);Check(session.HasSecondFactor,"live refresh preserves verified MFA assurance");
            Check((await Transport(session).ListAsync(slot)).Count==1,"live refreshed AAL2 token retains cloud access");
            // A second verification uses the next TOTP window, avoiding code replay.
            Console.WriteLine("Waiting for the next authenticator window (at most 31 seconds)…");
            await Task.Delay(TimeSpan.FromSeconds(31-DateTimeOffset.UtcNow.ToUnixTimeSeconds()%30));
            var fresh=await auth.VerifyTotp(passwordOnly,enrollment.Id,Code(enrollment.Secret));Check(fresh.HasSecondFactor,"live second-factor challenge completes a fresh sign-in");
            await auth.Unenroll(fresh,enrollment.Id);session=await auth.Refresh(fresh.RefreshToken);
            Check(!session.HasSecondFactor&&!(await auth.ListFactors(session)).HasVerified,"live removal plus refresh downgrades AAL and disables MFA");
            Check((await Transport(session).ListAsync(slot)).Count==1,"live removing MFA preserves encrypted cloud backups");
            await auth.SignOut(session.AccessToken);await Refuse(()=>auth.Refresh(session.RefreshToken),"live sign-out revokes the test session refresh token");
            Console.WriteLine($"RESULT: {passed} live MFA/Auth/Data API checks passed; no confirmation email sent.");
        }
        finally
        {
            try{await auth.SignOut(session.AccessToken);}catch(VaultException){}
            CryptographicOperations.ZeroMemory(key);Directory.Delete(folder,true);password="";
        }
    }
}
