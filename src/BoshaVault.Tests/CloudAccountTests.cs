using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using BoshaVault.Core;

public static class CloudAccountTests
{
    private sealed class Handler(Guid owner) : HttpMessageHandler
    {
        public bool Rejected, InvalidSession, Oversized;
        public string LastBody = "", ErrorCode = "";
        public readonly List<string> Paths = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            if (request.RequestUri.Scheme != "https" || request.Headers.GetValues("apikey").Single() != CloudProject.PublishableKey)
                throw new Exception("Unsafe cloud account request.");
            if (Oversized) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x',65537)) };
            return new HttpResponseMessage(Rejected ? HttpStatusCode.BadRequest : HttpStatusCode.OK)
            { Content = new StringContent(Rejected ? (ErrorCode.Length == 0 ? "server-error-with-private-token-never-echo" : JsonSerializer.Serialize(new { code = ErrorCode, message = "server-private-token-never-echo" })) : JsonSerializer.Serialize(new
                { user = new { id = owner }, access_token = InvalidSession ? "bad\r\ntoken" : CloudMfaTests.Token(owner,"aal1"), refresh_token = "synthetic-refresh-token-0123456789" })) };
        }
    }
    public static async Task Run()
    {
        await CloudMfaTests.Run();
        int passed=0;
        void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);passed++;}
        async Task Reject(Func<Task> action,string name){bool refused=false;try{await action();}catch(VaultException e){if(e.Message.Contains("private-token"))throw new Exception("Server credentials leaked into error.");refused=true;}Check(refused,name);}
        Guid owner=Guid.NewGuid(),vault=Guid.NewGuid();
        var handler=new Handler(owner);using var http=new HttpClient(handler);var account=new SupabaseCloudAccount(http);
        var signedIn=await account.SignIn("synthetic@example.invalid","not-a-real-password-123");
        Check(signedIn.OwnerId==owner,"cloud password sign-in reads server-issued owner identity");
        await account.Refresh(signedIn.RefreshToken);await account.SignOut(signedIn.AccessToken);
        Check(handler.Paths.Any(p=>p.Contains("grant_type=refresh_token"))&&handler.Paths.Any(p=>p.Contains("scope=local")),"cloud refresh and local session revocation use correct Auth endpoints");
        handler.Rejected=true;await Reject(()=>account.SignIn("synthetic@example.invalid","not-a-real-password-123"),"cloud error does not echo untrusted server secrets");handler.Rejected=false;
        handler.InvalidSession=true;await Reject(()=>account.SignIn("synthetic@example.invalid","not-a-real-password-123"),"cloud session rejects control characters in tokens");
        await Reject(()=>account.SignUp("synthetic@example.invalid","short"),"cloud account signup requires a long separate password");
        handler.InvalidSession=false;
        await account.SignUp("synthetic@example.invalid","synthetic separate signup password");
        using(var request=JsonDocument.Parse(handler.LastBody))Check(request.RootElement.GetProperty("code_challenge_method").GetString()=="s256"&&request.RootElement.GetProperty("code_challenge").GetString()!.Length==43&&!request.RootElement.TryGetProperty("code_verifier",out _),"cloud confirmation uses PKCE and sends no verifier");
        await account.ResendConfirmation("synthetic@example.invalid");
        using(var request=JsonDocument.Parse(handler.LastBody))Check(handler.Paths.Last().EndsWith("/resend")&&request.RootElement.GetProperty("type").GetString()=="signup"&&request.RootElement.GetProperty("code_challenge_method").GetString()=="s256"&&!request.RootElement.TryGetProperty("password",out _)&&!request.RootElement.TryGetProperty("code_verifier",out _),"confirmation resend uses signup type and PKCE without password or verifier");
        handler.Rejected=true;handler.ErrorCode="email_address_not_authorized";
        try{await account.SignUp("synthetic@example.invalid","synthetic separate signup password");throw new Exception("Expected signup rejection.");}catch(VaultException ex){Check(ex.Message.Contains("app owner")&&!ex.Message.Contains("server-private"),"restricted email sender reports actionable owner setup without exposing server content");}
        handler.Rejected=false;handler.ErrorCode="";
        handler.Oversized=true;await Reject(()=>account.SignIn("synthetic@example.invalid","synthetic password"),"cloud account refuses an oversized server response");handler.Oversized=false;
        var profile=new CloudProfile{OwnerId=owner,VaultId=vault,VaultSlot=Guid.NewGuid(),DeviceSlot=Guid.NewGuid(),Email="synthetic@example.invalid",RefreshToken=signedIn.RefreshToken,RecoveryKey=CloudSnapshotCodec.NewRecoveryKey()};
        string code=CloudPairing.Encode(profile);var decoded=CloudPairing.Decode(code,vault,owner);
        Check(decoded.VaultSlot==profile.VaultSlot&&decoded.RecoveryKey==profile.RecoveryKey,"cloud pairing code shares only vault slot/key and binds account and vault");
        await Reject(()=>{CloudPairing.Decode(code,Guid.NewGuid(),owner);return Task.CompletedTask;},"cloud pairing refuses replacing an unrelated local vault");
        await Reject(()=>{CloudPairing.Decode(code,vault,Guid.NewGuid());return Task.CompletedTask;},"cloud pairing refuses a different signed-in account");
        string folder=Path.Combine(Path.GetTempPath(),"bv-cloud-batch-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        try
        {
            using var a=await VaultSession.Create(folder+"/a","synthetic long cloud master passphrase",Guid.NewGuid().ToString());
            File.Copy(folder+"/a",folder+"/b");using var b=await VaultSession.Open(folder+"/b","synthetic long cloud master passphrase",Guid.NewGuid().ToString());
            b.Upsert(new VaultEntry{Title="synthetic remote edit",Password="only-a-synthetic-password"});byte[] remote=b.ExportEncrypted(),before=a.ExportEncrypted();
            await Reject(()=>{a.MergeCloudSnapshots([remote,new byte[200]]);return Task.CompletedTask;},"invalid later cloud snapshot aborts the whole local merge batch");
            Check(a.ExportEncrypted().SequenceEqual(before)&&a.Data.Entries.Count==0,"failed cloud batch leaves local encrypted file unchanged");
            a.MergeCloudSnapshots([remote]);Check(a.Data.Entries.Count==1,"validated cloud batch merges remote entries in one local commit");
            await b.ChangePassword("synthetic long cloud master passphrase","another synthetic strong cloud passphrase");before=a.ExportEncrypted();
            await Reject(()=>{a.MergeCloudSnapshots([b.ExportEncrypted()]);return Task.CompletedTask;},"cloud key rotation fails closed and requires explicit local reconciliation");
            Check(a.ExportEncrypted().SequenceEqual(before),"cloud key mismatch cannot change the local vault");
        }
        finally{Directory.Delete(folder,true);profile.Clear();}
        Console.WriteLine($"RESULT: {passed} cloud account/pairing/atomic merge checks passed.");
    }
}
