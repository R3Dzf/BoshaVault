using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BoshaVault.Core;

public static class CloudProject
{
    // Public client configuration. These values grant no access without an owner JWT.
    public const string Url = "https://ghmbcfqiomggoxgwghjs.supabase.co/";
    public const string PublishableKey = "sb_publishable__Kb9CeN7Bcx3lxz4VDL1hQ_27hJVAee";
}

public sealed class CloudProfile
{
    public int Version { get; set; } = 1;
    public Guid OwnerId { get; set; }
    public Guid VaultId { get; set; }
    public Guid VaultSlot { get; set; }
    public Guid DeviceSlot { get; set; }
    public string Email { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string RecoveryKey { get; set; } = "";
    public string LastSyncUtc { get; set; } = "";
    public bool AutoSync { get; set; } = true;
    public long KeyEpoch { get; set; }
    public string LastUploadedHash { get; set; } = "";
    public string LastOuterHash { get; set; } = "";
    public string DownloadedStamp { get; set; } = "";
    public string MergedStamp { get; set; } = "";

    public void Validate(Guid vaultId)
    {
        if (Email == null || RefreshToken == null || LastSyncUtc == null ||
            Version != 1 || OwnerId == Guid.Empty || VaultId != vaultId ||
            VaultSlot == Guid.Empty || DeviceSlot == Guid.Empty || Email.Length > 320 ||
            RefreshToken.Length is < 10 or > 8192 || RefreshToken.Any(char.IsControl) || LastSyncUtc.Length > 50)
            throw new VaultException("Cloud settings do not match this vault. Connect again.");
        byte[] key = CloudSnapshotCodec.ParseRecoveryKey(RecoveryKey);
        CryptographicOperations.ZeroMemory(key);
        foreach (string digest in new[] { LastUploadedHash, LastOuterHash, DownloadedStamp, MergedStamp })
            if (digest == null || (digest.Length != 0 && (digest.Length != 64 || digest.Any(c => !char.IsAsciiHexDigit(c)))))
                throw new VaultException("Invalid protected sync checkpoint.");
        if (KeyEpoch is < 0 or > 1_000_000) throw new VaultException("Invalid cloud key epoch.");
    }

    public void Clear() { RefreshToken = ""; RecoveryKey = ""; Email = ""; }
}

public sealed class CloudPairing
{
    public int Version { get; set; } = 1;
    public Guid OwnerId { get; set; }
    public Guid VaultId { get; set; }
    public Guid VaultSlot { get; set; }
    public string RecoveryKey { get; set; } = "";
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 4
    };

    public static string Encode(CloudProfile profile)
    {
        profile.Validate(profile.VaultId);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(new CloudPairing
        {
            OwnerId = profile.OwnerId, VaultId = profile.VaultId,
            VaultSlot = profile.VaultSlot, RecoveryKey = profile.RecoveryKey
        }, Json);
        try { return "BVCLOUD1:" + Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    public static CloudPairing Decode(string code, Guid vaultId, Guid ownerId)
    {
        if (code.Length is < 100 or > 1024 || !code.StartsWith("BVCLOUD1:", StringComparison.Ordinal))
            throw new VaultException("Paste a cloud connection code from your own other device.");
        byte[] data = [];
        try
        {
            string encoded = code[9..];
            if (encoded.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
                throw new FormatException();
            encoded = encoded.Replace('-', '+').Replace('_', '/');
            data = Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='));
            using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 4 });
            JsonOptions.NoDuplicates(document.RootElement);
            var pairing = JsonSerializer.Deserialize<CloudPairing>(data, Json) ?? throw new FormatException();
            if (pairing.Version != 1 || pairing.VaultSlot == Guid.Empty ||
                pairing.OwnerId != ownerId || pairing.VaultId == Guid.Empty || (vaultId != Guid.Empty && pairing.VaultId != vaultId))
                throw new VaultException("Use the same cloud account and the same vault on both devices. Transfer your vault over Wi-Fi or import its encrypted backup first.");
            byte[] key = CloudSnapshotCodec.ParseRecoveryKey(pairing.RecoveryKey);
            CryptographicOperations.ZeroMemory(key);
            return pairing;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        { throw new VaultException("Invalid cloud connection code.", ex); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }
}

public sealed partial class SupabaseCloudAccount(HttpClient http)
{
    public sealed record Session(Guid OwnerId, string AccessToken, string RefreshToken, string Assurance = "aal1")
    { public bool HasSecondFactor => Assurance == "aal2"; }

    internal static async Task<byte[]> ReadBounded(HttpContent content, int limit, CancellationToken ct)
    {
        if (content.Headers.ContentLength > limit) throw new VaultException("Cloud response exceeds its size limit.");
        using var stream = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] chunk = new byte[8192];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > limit) throw new VaultException("Cloud response exceeds its size limit.");
                output.Write(chunk, 0, read);
            }
            return output.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(chunk);
            if (output.TryGetBuffer(out var buffer)) CryptographicOperations.ZeroMemory(buffer.AsSpan());
        }
    }

    private Task<byte[]> Post(string path, object body, string? accessToken, CancellationToken ct, int limit = 65536)
        => Request(HttpMethod.Post, path, body, accessToken, ct, limit);

    private async Task<byte[]> Request(HttpMethod method, string path, object? body, string? accessToken, CancellationToken ct, int limit = 65536)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri(CloudProject.Url), path));
        request.Headers.Add("apikey", CloudProject.PublishableKey);
        if (accessToken != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        byte[] payload = body == null ? [] : JsonSerializer.SerializeToUtf8Bytes(body);
        try
        {
            if (body != null)
            {
                request.Content = new ByteArrayContent(payload);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            }
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                string code = ""; byte[] error = await ReadBounded(response.Content, 65536, ct).ConfigureAwait(false);
                try
                {
                    using var json = JsonDocument.Parse(error, new JsonDocumentOptions { MaxDepth = 16 });
                    if (json.RootElement.ValueKind == JsonValueKind.Object &&
                        (json.RootElement.TryGetProperty("error_code", out var value) || json.RootElement.TryGetProperty("code", out value)) && value.ValueKind == JsonValueKind.String) code = value.GetString() ?? "";
                }
                catch (JsonException) { }
                finally { CryptographicOperations.ZeroMemory(error); }
                string message = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? "Cloud account rate limit reached. Wait before trying again. Your offline vault is unchanged."
                    : code == "email_address_not_authorized"
                    ? "Email verification delivery is not enabled for this address yet. The app owner needs to configure the email sender. You can use your vault offline without an account."
                    : code == "email_not_confirmed"
                    ? "Confirm your account email first. Use Resend confirmation email if needed."
                    : code is "mfa_verification_failed" or "mfa_challenge_expired"
                    ? "The authenticator code is incorrect or expired. Use the current six-digit code and check your device clock."
                    : code is "insufficient_aal" or "mfa_factor_not_found" or "mfa_totp_enroll_not_enabled" or "mfa_totp_verify_not_enabled"
                    ? "Two-factor authentication could not be completed. Check your authenticator or reopen cloud security settings."
                    : path == "auth/v1/verify"
                    ? "The email confirmation code is incorrect or expired. Request a new confirmation email."
                    : path is "auth/v1/signup" or "auth/v1/resend"
                    ? "Cloud account verification could not be completed. Check the email and password requirements, or try again later. Your offline vault is unchanged."
                    : "Cloud sign-in was rejected. Check your email/password and confirm your email before signing in.";
                throw new VaultException(message);
            }
            return await ReadBounded(response.Content, limit, ct).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(payload); }
    }

    private static Session ParseSession(byte[] body)
    {
        try
        {
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement;
            JsonOptions.NoDuplicates(root);
            var owner = root.GetProperty("user").GetProperty("id").GetGuid();
            string access = root.GetProperty("access_token").GetString() ?? "";
            string refresh = root.GetProperty("refresh_token").GetString() ?? "";
            if (owner == Guid.Empty || access.Length is < 20 or > 8192 || refresh.Length is < 10 or > 8192 ||
                access.Any(c => char.IsControl(c)) || refresh.Any(c => char.IsControl(c))) throw new FormatException();
            return new(owner, access, refresh, ReadAssurance(access, owner));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        { throw new VaultException("Cloud account returned an invalid session.", ex); }
        finally { CryptographicOperations.ZeroMemory(body); }
    }

    public async Task<Session> SignIn(string email, string password, CancellationToken ct = default)
    {
        if (email.Length is < 3 or > 320 || !email.Contains('@') || password.Length is < 1 or > 1024)
            throw new VaultException("Enter your cloud account email and password.");
        return ParseSession(await Post("auth/v1/token?grant_type=password", new { email, password }, null, ct));
    }

    public async Task SignUp(string email, string password, CancellationToken ct = default)
    {
        if (email.Length is < 3 or > 320 || !email.Contains('@') || password.Length is < 16 or > 1024)
            throw new VaultException("Use a valid email and a separate cloud account password of at least 16 characters.");
        // Confirmation verifies the email; the user then signs in with a password.
        // PKCE prevents the confirmation redirect from carrying access/refresh tokens.
        byte[] verifier = RandomNumberGenerator.GetBytes(32);
        try
        {
            string encoded = Convert.ToBase64String(verifier).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(encoded))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            byte[] response = await Post("auth/v1/signup", new { email, password, code_challenge = challenge, code_challenge_method = "s256" }, null, ct);
            CryptographicOperations.ZeroMemory(response);
        }
        finally { CryptographicOperations.ZeroMemory(verifier); }
    }

    public async Task<Session> Refresh(string refreshToken, CancellationToken ct = default)
    {
        if (refreshToken.Length is < 10 or > 8192) throw new VaultException("Sign in to cloud again.");
        return ParseSession(await Post("auth/v1/token?grant_type=refresh_token", new { refresh_token = refreshToken }, null, ct));
    }

    public async Task ResendConfirmation(string email, CancellationToken ct = default)
    {
        if (email.Length is < 3 or > 320 || !email.Contains('@')) throw new VaultException("Enter your cloud account email.");
        byte[] verifier = RandomNumberGenerator.GetBytes(32);
        try
        {
            string encoded = Convert.ToBase64String(verifier).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(encoded))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            byte[] response = await Post("auth/v1/resend", new { type = "signup", email, code_challenge = challenge, code_challenge_method = "s256" }, null, ct);
            CryptographicOperations.ZeroMemory(response);
        }
        finally { CryptographicOperations.ZeroMemory(verifier); }
    }

    public async Task SignOut(string accessToken, CancellationToken ct = default)
    {
        byte[] response = await Post("auth/v1/logout?scope=local", new { }, accessToken, ct);
        CryptographicOperations.ZeroMemory(response);
    }
}
