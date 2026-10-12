using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace BoshaVault.Core;

/// <summary>
/// Untrusted Supabase relay for short-lived pairing. All rows are owner-scoped and
/// MFA-gated by RLS; cryptographic authentication remains purely end-to-end.
/// </summary>
public sealed class DevicePairingTransport(HttpClient http, string accessToken)
{
    public sealed record Row(Guid SessionId, Guid OwnerId, string InitiatorKey,
        string? JoinerKey, string? Ciphertext, string State, DateTimeOffset Expires);
    private static readonly Uri Base = new(new Uri(CloudProject.Url), "rest/v1/device_pairing_sessions");

    private async Task<byte[]> Send(HttpMethod method, string query, object? body, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, new Uri(Base + query));
        req.Headers.TryAddWithoutValidation("apikey", CloudProject.PublishableKey);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        byte[] payload = body == null ? [] : JsonSerializer.SerializeToUtf8Bytes(body);
        try
        {
            if (body != null)
            {
                req.Content = new ByteArrayContent(payload);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            byte[] bytes = await SupabaseCloudAccount.ReadBounded(response.Content, 8192, ct);
            if (!response.IsSuccessStatusCode)
            {
                CryptographicOperations.ZeroMemory(bytes);
                throw new VaultException("Device pairing relay rejected the request. Confirm account and 2FA, then retry.");
            }
            return bytes;
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }

    private static Row ReadRow(JsonElement element)
    {
        var r = new Row(
            element.GetProperty("session_id").GetGuid(),
            element.GetProperty("owner_id").GetGuid(),
            element.GetProperty("initiator_public_key").GetString() ?? "",
            element.GetProperty("joiner_public_key").ValueKind == JsonValueKind.Null ? null : element.GetProperty("joiner_public_key").GetString(),
            element.GetProperty("sealed_pairing").ValueKind == JsonValueKind.Null ? null : element.GetProperty("sealed_pairing").GetString(),
            element.GetProperty("state").GetString() ?? "",
            element.GetProperty("expires_at").GetDateTimeOffset());
        if (r.SessionId == Guid.Empty || r.OwnerId == Guid.Empty || r.Expires <= DateTimeOffset.UtcNow ||
            r.State is not ("waiting" or "requested" or "approved" or "completed" or "cancelled") ||
            r.Ciphertext?.Length > 4096 || r.JoinerKey?.Length > 180)
            throw new VaultException("Pairing session expired or returned invalid metadata.");
        DevicePairing.ValidatePublicKey(r.InitiatorKey);
        if (r.JoinerKey != null) DevicePairing.ValidatePublicKey(r.JoinerKey);
        return r;
    }

    private static Row ExactlyOne(byte[] bytes)
    {
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            JsonOptions.NoDuplicates(doc.RootElement);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 1)
                throw new VaultException("Device pairing request was not accepted or has expired.");
            return ReadRow(doc.RootElement[0]);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public async Task Create(DevicePairing session, Guid owner, CancellationToken ct)
    {
        byte[] result = await Send(HttpMethod.Post, "?select=session_id,owner_id,initiator_public_key,joiner_public_key,sealed_pairing,state,expires_at",
            new { session_id = session.SessionId, owner_id = owner, initiator_public_key = session.PublicKey, state = "waiting" }, ct);
        Row row = ExactlyOne(result);
        if (row.OwnerId != owner || row.SessionId != session.SessionId || row.InitiatorKey != session.PublicKey || row.State != "waiting")
            throw new VaultException("Device pairing relay returned an unexpected session.");
    }

    public async Task<Row?> Read(Guid id, CancellationToken ct)
    {
        byte[] data = await Send(HttpMethod.Get, $"?select=*&session_id=eq.{id:D}&limit=1", null, ct);
        try
        {
            using var doc = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 4 });
            JsonOptions.NoDuplicates(doc.RootElement);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() > 1)
                throw new VaultException("Invalid pairing relay response.");
            return doc.RootElement.GetArrayLength() == 0 ? null : ReadRow(doc.RootElement[0]);
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    public async Task Approve(DevicePairing session, Guid owner, string recipientPublicKey, string payload, CancellationToken ct)
    {
        var row = ExactlyOne(await Send(HttpMethod.Patch,
            $"?session_id=eq.{session.SessionId:D}&state=eq.requested&select=*",
            new { state = "approved", sealed_pairing = payload }, ct));
        if (row.OwnerId != owner || row.SessionId != session.SessionId || row.InitiatorKey != session.PublicKey ||
            row.JoinerKey != recipientPublicKey || row.Ciphertext != payload || row.State != "approved")
            throw new VaultException("Pairing was changed before approval. Start over.");
    }

    public async Task Cancel(Guid id, CancellationToken ct)
    {
        byte[] result = await Send(HttpMethod.Delete, $"?session_id=eq.{id:D}", null, ct);
        CryptographicOperations.ZeroMemory(result);
    }
}
