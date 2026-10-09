using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using BoshaVault.Core;

public static class CloudTransportTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<string> Requests=[];
        public bool WrongHash;
        private byte[] blob=[];
        private Guid slot;
        public void Configure(byte[] encrypted, Guid device)
        { blob=encrypted;slot=device; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            string body=req.Content==null?"":await req.Content.ReadAsStringAsync(ct);
            if(!req.Headers.Contains("apikey") ||
               req.Headers.Authorization?.Scheme!="Bearer")
                throw new Exception("Cloud transport request missing authentication.");
            Requests.Add(req.Method+" "+req.RequestUri+" "+body);
            if(req.Method==HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Created)
                { Content=new StringContent("",Encoding.UTF8,"application/json") };
            var row=new[] {new {
                device_slot=slot,
                ciphertext_base64=Convert.ToBase64String(blob),
                ciphertext_sha256=WrongHash?new string('0',64):
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(blob)).ToLowerInvariant()
            }};
            return new HttpResponseMessage(HttpStatusCode.OK)
            { Content=new StringContent(JsonSerializer.Serialize(row),Encoding.UTF8,"application/json") };
        }
    }
    public static async Task Run(byte[] opaqueBlob,Guid vaultSlot,Guid deviceSlot)
    {
        var fake=new FakeHandler();
        fake.Configure(opaqueBlob,deviceSlot);
        using var http=new HttpClient(fake);
        var store=new SupabaseSnapshotTransport(http,
            new Uri("https://example-project.supabase.co/"),
            "sb_publishable_dummy_test_key_not_real",
            _=>Task.FromResult("synthetic_auth_token_0123456789"));
        await store.UploadAsync(Guid.NewGuid(),vaultSlot,deviceSlot,opaqueBlob);
        var result=await store.ListAsync(vaultSlot);
        if(result.Count!=1 || result[0].DeviceSlot!=deviceSlot ||
           !result[0].Blob.SequenceEqual(opaqueBlob))
            throw new Exception("FAIL cloud encrypted transport roundtrip.");
        if(fake.Requests.Any(x=>x.Contains("BoshaVault.CloudOuter") || x.Contains("vault.boshavault")))
            throw new Exception("FAIL cloud transport leaked internal vault identifier.");
        fake.WrongHash=true;
        bool rejected=false;
        try {await store.ListAsync(vaultSlot);} catch(VaultException){rejected=true;}
        if(!rejected)throw new Exception("FAIL cloud checksum validation.");
        Console.WriteLine("PASS cloud transport uses Auth+RLS-targeted API and transfers only opaque encrypted content");
        Console.WriteLine("PASS cloud snapshot checksum mismatch rejected");
    }
}
