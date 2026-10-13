using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BoshaVault.Core;

public static class AutomaticCloudSyncTests
{
    private sealed class Server : HttpMessageHandler
    {
        public readonly Dictionary<Guid, byte[]> Rows = [];
        public readonly List<(string Outcome,long? Bytes,string? Error)> Outcomes = [];
        public bool Suspended;
        public int Uploads, Downloads;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path=request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/rpc/lockvyn_cloud_sync_status",StringComparison.Ordinal))
            {
                long bytes=Rows.Values.Sum(x=>(long)x.Length);
                var status=new { suspended=Suspended,snapshotCount=Rows.Count,ciphertextBytes=bytes,
                    quotaBytes=67_108_864L,remainingBytes=67_108_864L-bytes,maxSnapshots=64 };
                return new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(status),Encoding.UTF8,"application/json") };
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/rpc/lockvyn_register_cloud_device",StringComparison.Ordinal))
                return new(HttpStatusCode.OK) { Content=new StringContent(JsonSerializer.Serialize(Guid.NewGuid()),Encoding.UTF8,"application/json") };
            if (request.Method == HttpMethod.Post && path.EndsWith("/rpc/lockvyn_record_cloud_sync_outcome",StringComparison.Ordinal))
            {
                using var doc=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var result=doc.RootElement;
                Outcomes.Add((result.GetProperty("p_outcome").GetString()!,
                    result.GetProperty("p_ciphertext_bytes").ValueKind==JsonValueKind.Null?null:result.GetProperty("p_ciphertext_bytes").GetInt64(),
                    result.GetProperty("p_error_code").ValueKind==JsonValueKind.Null?null:result.GetProperty("p_error_code").GetString()));
                return new(HttpStatusCode.OK) { Content=new StringContent("true",Encoding.UTF8,"application/json") };
            }
            if (request.Method == HttpMethod.Post)
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var row = doc.RootElement;
                Rows[row.GetProperty("device_slot").GetGuid()] = Convert.FromBase64String(row.GetProperty("ciphertext_base64").GetString()!);
                Uploads++;
                return new(HttpStatusCode.Created) { Content = new StringContent("[]") };
            }
            bool full = request.RequestUri!.Query.Contains("ciphertext_base64", StringComparison.Ordinal);
            if (full) Downloads++;
            var data = Rows.Select(x => new Dictionary<string, string>
            {
                ["device_slot"] = x.Key.ToString("D"),
                ["ciphertext_sha256"] = Convert.ToHexString(SHA256.HashData(x.Value)).ToLowerInvariant(),
                ["ciphertext_base64"] = Convert.ToBase64String(x.Value)
            }.Where(pair => full || pair.Key != "ciphertext_base64").ToDictionary()).ToArray();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
        }
    }

    public static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "lockvyn-auto-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            string first = Path.Combine(root, "a.vault"), second = Path.Combine(root, "b.vault");
            string password = "synthetic master passphrase long enough", aId = Guid.NewGuid().ToString(), bId = Guid.NewGuid().ToString();
            using var a = await VaultSession.Create(first, password, aId);
            File.Copy(first, second); using var b = await VaultSession.Open(second, password, bId);
            var slot = Guid.NewGuid(); string recovery = CloudSnapshotCodec.NewRecoveryKey(); Guid owner = Guid.NewGuid(), id = Guid.Parse(a.Data.VaultId);
            CloudProfile Profile() => new() { OwnerId = owner, VaultId = id, VaultSlot = slot, DeviceSlot = Guid.NewGuid(), RecoveryKey = recovery,
                RefreshToken = "synthetic-refresh-token-only", Email = "synthetic@example.invalid" };
            var pa = Profile(); var pb = Profile(); pa.DeviceInventoryConsent=true;
            var server = new Server(); using var http = new HttpClient(server);
            var transport = new SupabaseSnapshotTransport(http, new Uri("https://synthetic.supabase.co/"), "synthetic_publishable_key_0123456789", _ => Task.FromResult("synthetic_bearer_token_0123456789"));
            byte[]? pendingA = null, pendingB = null;
            Task<AutomaticCloudSync.Result> Sync(VaultSession? vault, CloudProfile profile, bool aSide)
                => AutomaticCloudSync.RunAsync(transport, profile,
                    vault == null ? null : snapshots => { int conflicts = vault.MergeCloudSnapshots(snapshots); return Task.FromResult((vault.ExportEncrypted(), conflicts)); },
                    () => Task.FromResult(vault?.ExportEncrypted() ?? File.ReadAllBytes(aSide ? first : second)),
                    () => aSide ? pendingA?.ToArray() : pendingB?.ToArray(),
                    bytes => { if (aSide) pendingA = bytes?.ToArray(); else pendingB = bytes?.ToArray(); }, () => { });

            a.Upsert(new VaultEntry { Title = "From Windows", Password = "synthetic-secret-A-123456" });
            if (!(await Sync(a, pa, true)).Uploaded) throw new Exception("First device was not uploaded.");
            await Sync(b, pb, false);
            if (!b.Data.Entries.Any(x => x.Title == "From Windows")) throw new Exception("Second device did not receive the new login.");
            byte[] unchanged = b.ExportEncrypted(); int uploads = server.Uploads, downloads = server.Downloads;
            await Sync(b, pb, false);
            if (server.Uploads != uploads || server.Downloads != downloads || !unchanged.SequenceEqual(b.ExportEncrypted()))
                throw new Exception("Unchanged vault triggered a download, upload, or encrypted rewrite.");
            a.Upsert(new VaultEntry { Title = "Queued while phone locked", Password = "synthetic-secret-B-123456" });
            await Sync(a, pa, true);
            var locked = await Sync(null, pb, false);
            if (!locked.WaitingForUnlock || pendingB == null || b.Data.Entries.Any(x => x.Title == "Queued while phone locked"))
                throw new Exception("Locked vault did not stage only encrypted incoming data.");
            int stagedDownloads = server.Downloads;
            await Sync(b, pb, false);
            if (server.Downloads != stagedDownloads || pendingB != null || !b.Data.Entries.Any(x => x.Title == "Queued while phone locked"))
                throw new Exception("Unlock did not apply the staged encrypted update.");
            byte[] original = b.ExportEncrypted(); byte[] poisoned = server.Rows[pa.DeviceSlot].ToArray(); poisoned[^1] ^= 1; server.Rows[pa.DeviceSlot] = poisoned;
            bool rejected = false;
            try { await Sync(b, pb, false); } catch (VaultException) { rejected = true; }
            if (!rejected || !original.SequenceEqual(b.ExportEncrypted())) throw new Exception("Tampered cloud snapshot changed the vault.");
            if(!server.Outcomes.Any(x=>x.Outcome=="success"&&x.Bytes is > 0&&x.Error==null))
                throw new Exception("Explicitly opted-in sync did not report bounded success metadata.");
            var beforePauseUploads=server.Uploads;var beforePauseDownloads=server.Downloads;
            byte[] beforePauseVault=a.ExportEncrypted();server.Suspended=true;
            bool paused=false;
            try{await Sync(a,pa,true);}catch(VaultException ex)
            {
                paused=ex.Message.Contains("paused",StringComparison.OrdinalIgnoreCase)&&
                    ex.Message.Contains("preserved",StringComparison.OrdinalIgnoreCase);
            }
            if(!paused||server.Uploads!=beforePauseUploads||server.Downloads!=beforePauseDownloads||
               !beforePauseVault.SequenceEqual(a.ExportEncrypted()))
                throw new Exception("Sync suspension did not stop network snapshot work while preserving the local vault.");
            if(!server.Outcomes.Any(x=>x.Outcome=="failure"&&x.Error=="sync_suspended"))
                throw new Exception("Opted-in suspended sync did not report its bounded failure code.");
            Console.WriteLine("PASS automatic two-device sync, encrypted staging/merge, tamper rejection, opt-in telemetry, and paused preflight");
        }
        finally { Directory.Delete(root, true); }
    }
}
