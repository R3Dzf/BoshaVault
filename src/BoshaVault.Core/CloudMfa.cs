using System.Security.Cryptography;
using System.Text.Json;
using QRCoder;

namespace BoshaVault.Core;

public sealed partial class SupabaseCloudAccount
{
    public sealed record Factor(Guid Id, string Name, string Type, bool Verified);
    public sealed record Factors(IReadOnlyList<Factor> All)
    {
        public bool HasVerified => All.Any(f => f.Verified);
        public IReadOnlyList<Factor> Totp => All.Where(f => f.Verified && f.Type == "totp").ToArray();
        public bool RequiresChallenge(Session session) => HasVerified && !session.HasSecondFactor;
    }
    public sealed class TotpEnrollment(Guid id, string secret, string uri) : IDisposable
    {
        public Guid Id { get; } = id;
        public string Secret { get; private set; } = secret;
        public string Uri { get; private set; } = uri;
        public byte[] RenderQr()
        {
            if (Uri.Length == 0) throw new VaultException("Authenticator setup has closed.");
            using var data = QRCodeGenerator.GenerateQrCode(Uri, QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(data);
            return png.GetGraphic(6, drawQuietZones: true);
        }
        public void Dispose() { Secret = ""; Uri = ""; }
    }

    // This claim is only a UI hint from an HTTPS Auth response. The Data API verifies
    // the JWT signature and the database independently enforces the assurance level.
    private static string ReadAssurance(string token, Guid owner)
    {
        string[] parts = token.Split('.');
        if (parts.Length != 3) throw new FormatException();
        string encoded = parts[1].Replace('-', '+').Replace('_', '/');
        byte[] bytes = Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='));
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            JsonOptions.NoDuplicates(json.RootElement);
            if (json.RootElement.GetProperty("sub").GetGuid() != owner) throw new FormatException();
            string aal = json.RootElement.TryGetProperty("aal", out var value) ? value.GetString() ?? "" : "aal1";
            if (aal is not "aal1" and not "aal2") throw new FormatException();
            return aal;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public async Task<Factors> ListFactors(Session session, CancellationToken ct = default)
    {
        byte[] bytes = await Request(HttpMethod.Get, "auth/v1/user", null, session.AccessToken, ct);
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = json.RootElement; JsonOptions.NoDuplicates(root);
            if (root.GetProperty("id").GetGuid() != session.OwnerId) throw new FormatException();
            var factors = new List<Factor>();
            if (root.TryGetProperty("factors", out var list))
            {
                if (list.GetArrayLength() > 20) throw new FormatException();
                foreach (var item in list.EnumerateArray())
                {
                    var id = item.GetProperty("id").GetGuid();
                    string type = item.GetProperty("factor_type").GetString() ?? "";
                    string status = item.GetProperty("status").GetString() ?? "";
                    string name = item.TryGetProperty("friendly_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                    if (id == Guid.Empty || factors.Any(f => f.Id == id) || type.Length > 30 || status is not "verified" and not "unverified") throw new FormatException();
                    if (name.Length == 0 || name.Length > 120 || name.Any(char.IsControl)) name = "Authenticator " + id.ToString()[..8];
                    factors.Add(new(id, name, type, status == "verified"));
                }
            }
            return new(factors);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        { throw new VaultException("Cloud returned invalid authenticator details.", ex); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public async Task<TotpEnrollment> EnrollTotp(Session session, string email, CancellationToken ct = default)
    {
        if (email.Length is < 3 or > 320 || !email.Contains('@') || email.Any(char.IsControl)) throw new VaultException("Enter your cloud account email.");
        var factors = await ListFactors(session, ct);
        if (factors.RequiresChallenge(session)) throw new VaultException("Verify your current authenticator before adding another one.");
        // Abandoned enrollments are unverified and provide no second-factor protection.
        foreach (var factor in factors.All.Where(f => !f.Verified && f.Type == "totp")) await Unenroll(session, factor.Id, ct);
        // Auth includes a potentially large SVG even though we render our own QR.
        byte[] bytes = await Post("auth/v1/factors", new { factor_type = "totp", issuer = "Lockvyn", friendly_name = "Lockvyn " + Guid.NewGuid().ToString()[..8] }, session.AccessToken, ct, 524288);
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement; JsonOptions.NoDuplicates(root);
            var id = root.GetProperty("id").GetGuid();
            string secret = root.GetProperty("totp").GetProperty("secret").GetString() ?? "";
            if (id == Guid.Empty || root.GetProperty("type").GetString() != "totp" || secret.Length is < 16 or > 128 || secret.Any(c => c is not (>= 'A' and <= 'Z') and not (>= '2' and <= '7'))) throw new FormatException();
            // Render locally from the returned base32 secret, never from remote SVG/URLs.
            string uri = "otpauth://totp/" + System.Uri.EscapeDataString("Lockvyn:" + email) + "?secret=" + secret + "&issuer=Lockvyn&algorithm=SHA1&digits=6&period=30";
            return new(id, secret, uri);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        { throw new VaultException("Cloud returned invalid authenticator setup.", ex); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public async Task<Session> VerifyTotp(Session session, Guid factorId, string code, CancellationToken ct = default)
    {
        if (factorId == Guid.Empty || code.Length != 6 || code.Any(c => c is < '0' or > '9')) throw new VaultException("Enter the six-digit code from your authenticator.");
        byte[] bytes = await Post($"auth/v1/factors/{factorId}/challenge", new { }, session.AccessToken, ct);
        Guid challenge;
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 }); JsonOptions.NoDuplicates(json.RootElement);
            challenge = json.RootElement.GetProperty("id").GetGuid(); if (challenge == Guid.Empty) throw new FormatException();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        { throw new VaultException("Cloud returned an invalid authenticator challenge.", ex); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        var verified = ParseSession(await Post($"auth/v1/factors/{factorId}/verify", new { challenge_id = challenge, code }, session.AccessToken, ct));
        if (verified.OwnerId != session.OwnerId || !verified.HasSecondFactor) throw new VaultException("Two-factor verification did not return the expected account and assurance level.");
        return verified;
    }

    public async Task Unenroll(Session session, Guid factorId, CancellationToken ct = default)
    {
        if (factorId == Guid.Empty) throw new VaultException("Choose an authenticator.");
        byte[] bytes = await Request(HttpMethod.Delete, $"auth/v1/factors/{factorId}", null, session.AccessToken, ct);
        CryptographicOperations.ZeroMemory(bytes);
    }

    public async Task VerifyEmail(string email, string code, CancellationToken ct = default)
    {
        if (email.Length is < 3 or > 320 || !email.Contains('@') || code.Length is < 6 or > 10 || code.Any(c => c is < '0' or > '9')) throw new VaultException("Enter your email and its confirmation code (6–10 digits).");
        byte[] bytes = await Post("auth/v1/verify", new { email, token = code, type = "signup" }, null, ct);
        // Confirmation never connects a cloud profile or skips the subsequent MFA login.
        CryptographicOperations.ZeroMemory(bytes);
    }
}
