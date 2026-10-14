using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BoshaVault.Core;

/// <summary>
/// Optional transport for already E2EE-encrypted snapshots. Never accepts
/// plaintext vault records or recovery keys. Authentication is supplied by
/// a separate Supabase Auth session supplied by the opt-in cloud dialog.
/// </summary>
public sealed class SupabaseSnapshotTransport(HttpClient http, Uri projectUrl,
    string publishableKey, Func<CancellationToken,Task<string>> userAccessToken)
{
    public sealed class SnapshotRow
    {
        [JsonPropertyName("owner_id")] public Guid OwnerId { get; set; }
        [JsonPropertyName("vault_slot")] public Guid VaultSlot { get; set; }
        [JsonPropertyName("device_slot")] public Guid DeviceSlot { get; set; }
        [JsonPropertyName("ciphertext_base64")] public string CiphertextBase64 { get; set; } = "";
        [JsonPropertyName("ciphertext_sha256")] public string CiphertextSha256 { get; set; } = "";
    }
    public sealed record RemoteEncryptedSnapshot(Guid DeviceSlot,byte[] Blob);
    public sealed record RemoteSnapshotMetadata(Guid DeviceSlot,string Digest);
    public sealed record CloudSyncStatus(bool Suspended,long SnapshotCount,long CiphertextBytes,long QuotaBytes,long RemainingBytes);

    private async Task<byte[]> InventoryRpcAsync(string function, object body, CancellationToken ct)
    {
        using var req = await MakeRequest(HttpMethod.Post, "rest/v1/rpc/" + function, ct);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode) throw new VaultException("Cloud session inventory could not be updated.");
        return await SupabaseCloudAccount.ReadBounded(res.Content, 8192, ct);
    }

    public async Task<CloudSyncStatus> GetCloudSyncStatusAsync(CancellationToken ct = default)
    {
        byte[] response=await InventoryRpcAsync("lockvyn_cloud_sync_status",new { },ct);
        try
        {
            using var json=JsonDocument.Parse(response,new JsonDocumentOptions{MaxDepth=4});
            JsonOptions.NoDuplicates(json.RootElement);
            var root=json.RootElement;
            if(root.ValueKind!=JsonValueKind.Object ||
               !root.TryGetProperty("suspended",out var suspended) ||
               suspended.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
               !root.TryGetProperty("snapshotCount",out var count) || !count.TryGetInt64(out long snapshotCount) ||
               !root.TryGetProperty("ciphertextBytes",out var used) || !used.TryGetInt64(out long ciphertextBytes) ||
               !root.TryGetProperty("quotaBytes",out var quota) || !quota.TryGetInt64(out long quotaBytes) ||
               !root.TryGetProperty("remainingBytes",out var remaining) || !remaining.TryGetInt64(out long remainingBytes) ||
               snapshotCount is < 0 or > 64 || ciphertextBytes < 0 ||
               quotaBytes is < 1 or > 67_108_864 || remainingBytes < 0 || remainingBytes > quotaBytes)
                throw new VaultException("Cloud returned invalid sync status.");
            return new(suspended.GetBoolean(),snapshotCount,ciphertextBytes,quotaBytes,remainingBytes);
        }
        catch(JsonException ex){throw new VaultException("Cloud returned invalid sync status.",ex);}
        finally{CryptographicOperations.ZeroMemory(response);}
    }

    public async Task RecordCloudSyncOutcomeAsync(string outcome,long? ciphertextBytes,
        string? errorCode,CancellationToken ct=default)
    {
        if(outcome is not ("success" or "failure") ||
           ciphertextBytes is < 0 or > 67_108_864 ||
           errorCode is not (null or "quota_exceeded" or "sync_suspended" or "network" or "auth" or "transport" or "unknown") ||
           outcome=="success" && errorCode is not null ||
           outcome=="failure" && errorCode is null)
            throw new VaultException("Invalid bounded cloud sync result.");
        byte[] response=await InventoryRpcAsync("lockvyn_record_cloud_sync_outcome",new
        {
            p_outcome=outcome,p_ciphertext_bytes=ciphertextBytes,p_error_code=errorCode
        },ct);
        try
        {
            using var json=JsonDocument.Parse(response,new JsonDocumentOptions{MaxDepth=2});
            if(json.RootElement.ValueKind!=JsonValueKind.True)
                throw new VaultException("Cloud did not accept the sync result.");
        }
        catch(JsonException ex){throw new VaultException("Cloud returned an invalid sync result.",ex);}
        finally{CryptographicOperations.ZeroMemory(response);}
    }

    public async Task<bool> GetAnalyticsConsentAsync(CancellationToken ct = default)
    {
        byte[] response=await InventoryRpcAsync("lockvyn_analytics_consent_status",new { },ct);
        try
        {
            using var json=JsonDocument.Parse(response,new JsonDocumentOptions{MaxDepth=2});
            if(json.RootElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new VaultException("Cloud returned an invalid privacy choice.");
            return json.RootElement.GetBoolean();
        }
        catch(JsonException ex){throw new VaultException("Cloud returned an invalid privacy choice.",ex);}
        finally{CryptographicOperations.ZeroMemory(response);}
    }

    public async Task SetAnalyticsConsentAsync(bool enabled,CancellationToken ct=default)
    {
        byte[] response=await InventoryRpcAsync("lockvyn_analytics_set_consent",new { p_enabled=enabled },ct);
        try
        {
            using var json=JsonDocument.Parse(response,new JsonDocumentOptions{MaxDepth=2});
            if(json.RootElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
               json.RootElement.GetBoolean()!=enabled)
                throw new VaultException("Cloud did not confirm the privacy choice.");
        }
        catch(JsonException ex){throw new VaultException("Cloud returned an invalid privacy choice.",ex);}
        finally{CryptographicOperations.ZeroMemory(response);}
    }

    public async Task RecordAnalyticsEventAsync(string eventType,string platform,string appVersion,
        string? outcome,string? resourceBucket,CancellationToken ct=default)
    {
        bool versionValid=appVersion=="unknown"||
            Regex.IsMatch(appVersion,@"^([0-9]{1,5}(\.[0-9]{1,5}){0,3}(-[A-Za-z0-9.-]{1,20})?)$");
        bool validActivity=eventType=="app_active"&&outcome is null&&resourceBucket is null;
        bool validSync=eventType=="sync_result"&&(outcome is "success" or "failure")&&
            (resourceBucket is "none" or "lt_1mib" or "1_10mib" or "gt_10mib");
        if(platform is not ("windows" or "android")||!versionValid||(!validActivity&&!validSync))
            throw new VaultException("Invalid privacy analytics event.");
        byte[] response=await InventoryRpcAsync("lockvyn_analytics_record_event",new
        {
            p_event=eventType,p_platform=platform,p_app_version=appVersion,
            p_outcome=outcome,p_resource_bucket=resourceBucket
        },ct);
        try
        {
            using var json=JsonDocument.Parse(response,new JsonDocumentOptions{MaxDepth=2});
            if(json.RootElement.ValueKind!=JsonValueKind.True)
                throw new VaultException("Cloud did not accept the privacy analytics event.");
        }
        catch(JsonException ex){throw new VaultException("Cloud returned an invalid analytics response.",ex);}
        finally{CryptographicOperations.ZeroMemory(response);}
    }

    public async Task<Guid> RegisterCloudDeviceAsync(string platform, string osVersion,
        string appVersion, string displayName, CancellationToken ct = default)
    {
        static bool Valid(string value, int max) =>
            value.Length is > 0 && value.Length <= max && !value.Any(char.IsControl) && !string.IsNullOrWhiteSpace(value);
        if (platform is not ("windows" or "android" or "macos" or "ios" or "linux" or "other") ||
            !Valid(osVersion, 64) || !Valid(appVersion, 64) || !Valid(displayName, 80))
            throw new VaultException("Invalid cloud session metadata.");
        byte[] response = await InventoryRpcAsync("lockvyn_register_cloud_device", new
        {
            p_consent = true, p_platform = platform, p_os_version = osVersion,
            p_app_version = appVersion, p_display_name = displayName, p_schema_version = 1
        }, ct);
        try
        {
            using var json = JsonDocument.Parse(response, new JsonDocumentOptions { MaxDepth = 2 });
            if (json.RootElement.ValueKind != JsonValueKind.String ||
                !json.RootElement.TryGetGuid(out Guid id) || id == Guid.Empty)
                throw new VaultException("Cloud returned invalid session inventory metadata.");
            return id;
        }
        catch (JsonException ex) { throw new VaultException("Cloud returned invalid session inventory metadata.", ex); }
        finally { CryptographicOperations.ZeroMemory(response); }
    }

    public async Task RecordCloudDeviceSyncAsync(CancellationToken ct = default)
    {
        byte[] response = await InventoryRpcAsync("lockvyn_record_cloud_device_sync", new { }, ct);
        try
        {
            using var json = JsonDocument.Parse(response, new JsonDocumentOptions { MaxDepth = 2 });
            if (json.RootElement.ValueKind != JsonValueKind.True)
                throw new VaultException("Cloud did not accept the session sync timestamp.");
        }
        catch (JsonException ex) { throw new VaultException("Cloud returned an invalid session sync response.", ex); }
        finally { CryptographicOperations.ZeroMemory(response); }
    }

    public async Task UnregisterCloudDeviceAsync(CancellationToken ct = default)
    {
        byte[] response = await InventoryRpcAsync("lockvyn_unregister_cloud_device", new { }, ct);
        try
        {
            using var json = JsonDocument.Parse(response, new JsonDocumentOptions { MaxDepth = 2 });
            if (json.RootElement.ValueKind != JsonValueKind.True)
                throw new VaultException("Cloud did not accept the session inventory withdrawal.");
        }
        catch (JsonException ex) { throw new VaultException("Cloud returned an invalid session inventory response.", ex); }
        finally { CryptographicOperations.ZeroMemory(response); }
    }

    public async Task<IReadOnlyList<RemoteSnapshotMetadata>> ListMetadataAsync(Guid vaultSlot, CancellationToken ct = default)
    {
        if (vaultSlot == Guid.Empty) throw new VaultException("Invalid cloud vault identifier.");
        using var req = await MakeRequest(HttpMethod.Get, "rest/v1/opaque_device_snapshots?select=device_slot,ciphertext_sha256&vault_slot=eq." + vaultSlot.ToString("D") + "&order=device_slot&limit=64", ct);
        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode) throw new VaultException("Cloud sync needs sign-in or could not be reached. Open Sync to check your account.");
        byte[] bytes = await SupabaseCloudAccount.ReadBounded(res.Content, 65536, ct);
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            JsonOptions.NoDuplicates(json.RootElement);
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() > 64) throw new VaultException("Invalid sync metadata.");
            var result = new List<RemoteSnapshotMetadata>(); var ids = new HashSet<Guid>();
            foreach (var row in json.RootElement.EnumerateArray())
            {
                Guid id = row.GetProperty("device_slot").GetGuid(); string digest = row.GetProperty("ciphertext_sha256").GetString() ?? "";
                if (id == Guid.Empty || !ids.Add(id) || digest.Length != 64 || digest.Any(c => !char.IsAsciiHexDigit(c))) throw new VaultException("Invalid sync metadata.");
                result.Add(new(id, digest.ToLowerInvariant()));
            }
            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { throw new VaultException("Invalid sync metadata.", ex); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private Uri BaseUri
    {
        get
        {
            if (projectUrl.Scheme!="https" || projectUrl.Port!=443 ||
                !projectUrl.AbsolutePath.Equals("/") || !string.IsNullOrEmpty(projectUrl.UserInfo) ||
                !projectUrl.DnsSafeHost.EndsWith(".supabase.co",StringComparison.OrdinalIgnoreCase))
                throw new VaultException("Use a standard HTTPS Supabase project URL.");
            return projectUrl;
        }
    }
    private async Task<HttpRequestMessage> MakeRequest(HttpMethod verb,
        string path, CancellationToken ct)
    {
        _ = BaseUri;
        if (publishableKey.Length is < 10 or > 512 || publishableKey.Contains('\r') || publishableKey.Contains('\n'))
            throw new VaultException("Invalid Supabase publishable key.");
        string bearer=await userAccessToken(ct).ConfigureAwait(false);
        if (bearer.Length is < 20 or > 8192 || bearer.Contains('\r') || bearer.Contains('\n'))
            throw new VaultException("A valid signed-in Supabase Auth session is required.");
        var request=new HttpRequestMessage(verb,new Uri(BaseUri,path));
        request.Headers.TryAddWithoutValidation("apikey",publishableKey);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
        return request;
    }
    // One row per device, so offline edits on two devices cannot overwrite each
    // other. Their encrypted vaults are downloaded and merged LOCALLY using the
    // existing authenticated revision-vector merge engine.
    public async Task UploadAsync(Guid owner,Guid vaultSlot,Guid deviceSlot,
        ReadOnlyMemory<byte> outerEncryptedBlob,CancellationToken ct=default)
    {
        if (owner==Guid.Empty || vaultSlot==Guid.Empty || deviceSlot==Guid.Empty ||
            outerEncryptedBlob.Length is < 130 or > CloudSnapshotCodec.MaxInnerBytes+29)
            throw new VaultException("Invalid encrypted cloud snapshot size/identity.");
        string payload=Convert.ToBase64String(outerEncryptedBlob.Span);
        if(payload.Length>2_800_000)throw new VaultException("Cloud upload exceeds database limit.");
        string digest=Convert.ToHexString(SHA256.HashData(outerEncryptedBlob.Span)).ToLowerInvariant();
        using var req=await MakeRequest(HttpMethod.Post,
            "rest/v1/opaque_device_snapshots?on_conflict=owner_id,vault_slot,device_slot",ct);
        req.Headers.TryAddWithoutValidation("Prefer","resolution=merge-duplicates,return=minimal");
        req.Content=new StringContent(JsonSerializer.Serialize(new SnapshotRow
        {
            OwnerId=owner,VaultSlot=vaultSlot,DeviceSlot=deviceSlot,
            CiphertextBase64=payload,CiphertextSha256=digest
        }),Encoding.UTF8,"application/json");
        using var res=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!res.IsSuccessStatusCode)
        {
            string code=await SafeDatabaseErrorCodeAsync(res,ct);
            string message=code switch
            {
                "BV001"=>"Cloud storage limit reached. Nothing was deleted; your local vault is unchanged.",
                "BV002"=>"Cloud sync is paused. Offline vault data and encrypted cloud snapshots are preserved.",
                _=>"Encrypted cloud upload was rejected. Check your cloud session and try again."
            };
            throw new VaultException(message);
        }
    }

    private static async Task<string> SafeDatabaseErrorCodeAsync(HttpResponseMessage response,CancellationToken ct)
    {
        byte[] bytes=await SupabaseCloudAccount.ReadBounded(response.Content,8192,ct);
        try
        {
            using var json=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=3});
            if(json.RootElement.ValueKind!=JsonValueKind.Object ||
               !json.RootElement.TryGetProperty("code",out var code) || code.ValueKind!=JsonValueKind.String)
                return "";
            string value=code.GetString() ?? "";
            return value is "BV001" or "BV002" ? value : "";
        }
        catch(JsonException){return "";}
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }

    public async Task<IReadOnlyList<RemoteEncryptedSnapshot>> ListAsync(Guid vaultSlot,
        CancellationToken ct=default)
    {
        if(vaultSlot==Guid.Empty)throw new VaultException("Invalid cloud vault identifier.");
        string uri="rest/v1/opaque_device_snapshots?select=device_slot,ciphertext_base64,ciphertext_sha256"+
            "&vault_slot=eq."+Uri.EscapeDataString(vaultSlot.ToString("D"))+
            "&order=updated_at.desc&limit=64";
        using var req=await MakeRequest(HttpMethod.Get,uri,ct);
        using var res=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!res.IsSuccessStatusCode)throw new VaultException("Encrypted cloud download rejected by Supabase.");
        byte[] content=await SupabaseCloudAccount.ReadBounded(res.Content,16_000_000,ct);
        try
        {
            if(content.Length>16_000_000)throw new VaultException("Too many cloud snapshots; refusing unbounded response.");
            using var json=JsonDocument.Parse(content,new JsonDocumentOptions{MaxDepth=6});
            if(json.RootElement.ValueKind!=JsonValueKind.Array || json.RootElement.GetArrayLength()>64)
                throw new VaultException("Invalid cloud result.");
            List<RemoteEncryptedSnapshot> results=[];
            foreach(JsonElement row in json.RootElement.EnumerateArray())
            {
                Guid device=row.GetProperty("device_slot").GetGuid();
                string b64=row.GetProperty("ciphertext_base64").GetString() ?? "";
                string digest=row.GetProperty("ciphertext_sha256").GetString() ?? "";
                if(device==Guid.Empty || b64.Length>2_800_000 || digest.Length!=64)
                    throw new VaultException("Invalid cloud snapshot metadata.");
                byte[] blob=Convert.FromBase64String(b64);
                if(blob.Length>CloudSnapshotCodec.MaxInnerBytes+29 ||
                    !CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(Convert.ToHexString(SHA256.HashData(blob)).ToLowerInvariant()),
                        Encoding.ASCII.GetBytes(digest)))
                    throw new VaultException("Cloud snapshot checksum mismatch.");
                results.Add(new(device,blob));
            }
            return results;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            throw new VaultException("Invalid cloud transport response.",ex);
        }
        finally { CryptographicOperations.ZeroMemory(content); }
    }
}
