using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoshaVault.Core;

/// <summary>Opt-in tests using temporary synthetic users only. Never run against real vaults.</summary>
public static class CloudLiveTests
{
    public static async Task Run(string configuration)
    {
        using var config=JsonDocument.Parse(File.ReadAllBytes(configuration));var input=config.RootElement;
        using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};
        var account=new SupabaseCloudAccount(http);
        var a=await account.SignIn(input.GetProperty("emailA").GetString()!,input.GetProperty("passwordA").GetString()!);
        var b=await account.SignIn(input.GetProperty("emailB").GetString()!,input.GetProperty("passwordB").GetString()!);
        int passed=0;
        void Check(bool good,string name){if(!good)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);passed++;}
        async Task Refuse(Func<Task> run,string name){bool refused=false;try{await run();}catch(VaultException){refused=true;}Check(refused,name);}
        string root=Path.Combine(Path.GetTempPath(),"bv-live-synthetic-"+Guid.NewGuid());Directory.CreateDirectory(root);
        byte[] key=CloudSnapshotCodec.ParseRecoveryKey(CloudSnapshotCodec.NewRecoveryKey());
        try
        {
            Check(a.OwnerId!=b.OwnerId,"live Auth creates distinct server-signed owner sessions");
            using var local=await VaultSession.Create(root+"/vault","only a synthetic cloud test passphrase",Guid.NewGuid().ToString());
            local.Upsert(new VaultEntry{Title="Synthetic gateway test",Password="OnlySyntheticTestData-2345!"});
            Guid slot=Guid.NewGuid(),device=Guid.NewGuid();byte[] inner=local.ExportEncrypted(),blob=CloudSnapshotCodec.Seal(inner,key,slot,device);
            var sa=new SupabaseSnapshotTransport(http,new Uri(CloudProject.Url),CloudProject.PublishableKey,_=>Task.FromResult(a.AccessToken));
            var sb=new SupabaseSnapshotTransport(http,new Uri(CloudProject.Url),CloudProject.PublishableKey,_=>Task.FromResult(b.AccessToken));
            await sa.UploadAsync(a.OwnerId,slot,device,blob);var own=await sa.ListAsync(slot);
            Check(own.Count==1&&CloudSnapshotCodec.Open(own[0].Blob,key,slot,device).SequenceEqual(inner),"live encrypted upload/download/decryption roundtrip");
            Check((await sb.ListAsync(slot)).Count==0,"live user B cannot read user A's cloud vault");
            await Refuse(()=>sb.UploadAsync(a.OwnerId,slot,device,blob),"live cross-owner INSERT rejected by Data API/RLS");
            await sb.UploadAsync(b.OwnerId,slot,device,blob);Check((await sb.ListAsync(slot)).Count==1,"live user B can write its own isolated row");
            async Task<HttpResponseMessage> Request(HttpMethod method,string path,string? token,string? body=null)
            {
                using var request=new HttpRequestMessage(method,CloudProject.Url+path);request.Headers.Add("apikey",CloudProject.PublishableKey);
                if(token!=null)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                request.Headers.Add("Prefer","return=representation");
                if(body!=null)request.Content=new StringContent(body,Encoding.UTF8,"application/json");
                return await http.SendAsync(request);
            }
            string row="rest/v1/opaque_device_snapshots?owner_id=eq."+a.OwnerId+"&vault_slot=eq."+slot;
            using(var response=await Request(HttpMethod.Get,row,null))Check(!response.IsSuccessStatusCode,"live anonymous request cannot read cloud rows");
            using(var response=await Request(HttpMethod.Get,row,"invalid.jwt.token"))Check(!response.IsSuccessStatusCode,"live invalid JWT is rejected by gateway");
            using(var response=await Request(HttpMethod.Patch,row,b.AccessToken,"{\"ciphertext_sha256\":\""+new string('0',64)+"\"}"))
                Check(response.IsSuccessStatusCode&&(await response.Content.ReadAsStringAsync()).Trim()=="[]","live cross-owner UPDATE changes zero rows");
            using(var response=await Request(HttpMethod.Delete,row,b.AccessToken))
                Check(response.IsSuccessStatusCode&&(await response.Content.ReadAsStringAsync()).Trim()=="[]","live cross-owner DELETE removes zero rows");
            Check((await sa.ListAsync(slot))[0].Blob.SequenceEqual(blob),"cross-owner operations preserve the owner's encrypted snapshot");
            a=await account.Refresh(a.RefreshToken);Check(a.OwnerId==localOwner(input,"idA"),"live refresh rotation keeps verified owner identity");
            await account.SignOut(a.AccessToken);
            await Refuse(()=>account.Refresh(a.RefreshToken),"live sign-out revokes this session's refresh token");
            await account.SignOut(b.AccessToken);
            Console.WriteLine($"RESULT: {passed} live Auth/Data API/E2EE checks passed using temporary synthetic users.");
        }
        finally{CryptographicOperations.ZeroMemory(key);Directory.Delete(root,true);}
    }
    private static Guid localOwner(JsonElement json,string name)=>json.GetProperty(name).GetGuid();
}
