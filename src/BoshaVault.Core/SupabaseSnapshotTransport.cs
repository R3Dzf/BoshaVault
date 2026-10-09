using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoshaVault.Core;

/// <summary>
/// Optional transport for already E2EE-encrypted snapshots. Never accepts
/// plaintext vault records or recovery keys. Authentication is supplied by
/// a separate Supabase Auth session that has not yet been wired into the UI.
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
        if(!res.IsSuccessStatusCode)throw new VaultException("Encrypted cloud upload rejected by Supabase.");
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
        byte[] content=await res.Content.ReadAsByteArrayAsync(ct);
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
        catch (Exception ex) when (ex is JsonException or FormatException or KeyNotFoundException)
        {
            throw new VaultException("Invalid cloud transport response.",ex);
        }
        finally { CryptographicOperations.ZeroMemory(content); }
    }
}
