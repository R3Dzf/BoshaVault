using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BoshaVault.Core;

/// <summary>
/// Short-lived ECDH P-256 pairing. The QR carries ONLY the initiator's public key,
/// random session identifier and expiry. No recovery key or cloud token is in the QR.
/// The six-digit SAS MUST be compared on both devices before the initiator releases
/// the encrypted pairing payload. It is NOT a password and is never sent to Supabase.
/// </summary>
public sealed class DevicePairing : IDisposable
{
    public const string Prefix = "BVPAIR2:";
    private const int MaxCode = 1024;
    private readonly ECDiffieHellman ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    private bool disposed;
    private bool used;

    public Guid SessionId { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string PublicKey { get; }

    public DevicePairing()
    {
        SessionId = Guid.NewGuid();
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2);
        PublicKey = Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo());
    }

    public string Invitation()
    {
        EnsureLive();
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 2, sessionId = SessionId,
            expiresUnix = ExpiresAt.ToUnixTimeSeconds(), publicKey = PublicKey
        });
        try { return Prefix + Base64Url(data); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    public static (Guid Id, DateTimeOffset Expiry, string Key) ParseInvitation(string code)
    {
        if (code is null || !code.StartsWith(Prefix, StringComparison.Ordinal) ||
            code.Length > MaxCode || code.Length < 100)
            throw new VaultException("Scan a current Lockvyn Add device QR.");
        byte[] raw = [];
        try
        {
            raw = FromBase64Url(code[Prefix.Length..], 512);
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 3 });
            JsonOptions.NoDuplicates(document.RootElement);
            var value = document.RootElement;
            if (value.GetProperty("version").GetInt32() != 2) throw new FormatException();
            Guid id = value.GetProperty("sessionId").GetGuid();
            long unix = value.GetProperty("expiresUnix").GetInt64();
            string key = value.GetProperty("publicKey").GetString() ?? "";
            var expiry = DateTimeOffset.FromUnixTimeSeconds(unix);
            if (id == Guid.Empty || expiry < DateTimeOffset.UtcNow ||
                expiry > DateTimeOffset.UtcNow.AddMinutes(2).AddSeconds(45))
                throw new VaultException("Pairing QR expired. Generate a new code on your computer.");
            ValidatePublicKey(key);
            return (id, expiry, key);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or ArgumentException)
        { throw new VaultException("Invalid or expired device pairing QR.", ex); }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }

    public static void ValidatePublicKey(string encoded)
    {
        byte[] buffer = [];
        try
        {
            if (encoded.Length is < 110 or > 180) throw new FormatException();
            buffer = Convert.FromBase64String(encoded);
            using var peer = ECDiffieHellman.Create();
            peer.ImportSubjectPublicKeyInfo(buffer, out int consumed);
            var parameters = peer.ExportParameters(false);
            if (consumed != buffer.Length || parameters.Q.X?.Length != 32 ||
                parameters.Q.Y?.Length != 32 || parameters.Curve.Oid.Value != "1.2.840.10045.3.1.7")
                throw new FormatException();
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        { throw new VaultException("Invalid device pairing public key.", ex); }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    public string SecurityCode(string otherPublicKey, string initiatorPublicKey)
    {
        byte[] secret = Derive(otherPublicKey, initiatorPublicKey, "SAS");
        try
        {
            uint value = BinaryPrimitives.ReadUInt32BigEndian(secret) % 1_000_000;
            return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    public string SealPairing(string pairingCode, string recipientPublicKey)
    {
        EnsureLive();
        if (used) throw new VaultException("This pairing session was already approved.");
        byte[] key = Derive(recipientPublicKey, PublicKey, "AES"), nonce = RandomNumberGenerator.GetBytes(12);
        byte[] plaintext = Encoding.UTF8.GetBytes(pairingCode);
        byte[] ciphertext = new byte[plaintext.Length], tag = new byte[16];
        try
        {
            if (plaintext.Length is < 100 or > 1024 || !pairingCode.StartsWith("BVCLOUD1:", StringComparison.Ordinal))
                throw new VaultException("Invalid vault pairing payload.");
            using (var aes = new AesGcm(key, 16))
                aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(PublicKey, recipientPublicKey));
            used = true;
            byte[] combined = new byte[nonce.Length + ciphertext.Length + tag.Length];
            try
            {
                nonce.CopyTo(combined, 0);
                ciphertext.CopyTo(combined, nonce.Length);
                tag.CopyTo(combined, nonce.Length + ciphertext.Length);
                return Convert.ToBase64String(combined);
            }
            finally { CryptographicOperations.ZeroMemory(combined); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(nonce);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] AssociatedData(string initiator, string recipient) =>
        Encoding.UTF8.GetBytes($"Lockvyn.Pairing.v2|{SessionId:D}|{initiator}|{recipient}");

    private byte[] Derive(string peerPublicKey, string initiatorPublicKey, string purpose)
    {
        EnsureLive();
        ValidatePublicKey(peerPublicKey);
        ValidatePublicKey(initiatorPublicKey);
        string recipient = PublicKey == initiatorPublicKey ? peerPublicKey : PublicKey;
        byte[] der = Convert.FromBase64String(peerPublicKey), shared = [];
        try
        {
            using var peer = ECDiffieHellman.Create();
            peer.ImportSubjectPublicKeyInfo(der, out _);
            shared = ecdh.DeriveRawSecretAgreement(peer.PublicKey);
            byte[] salt = SHA256.HashData(AssociatedData(initiatorPublicKey, recipient));
            try
            {
                byte[] prk = HMACSHA256.HashData(salt, shared);
                try { return HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes(purpose)); }
                finally { CryptographicOperations.ZeroMemory(prk); }
            }
            finally { CryptographicOperations.ZeroMemory(salt); }
        }
        finally { CryptographicOperations.ZeroMemory(der); CryptographicOperations.ZeroMemory(shared); }
    }

    private void EnsureLive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (DateTimeOffset.UtcNow >= ExpiresAt)
            throw new VaultException("Pairing expired. Please start Add device again.");
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string text, int maxBytes)
    {
        if (text.Length > maxBytes * 2 || text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new FormatException();
        string base64 = text.Replace('-', '+').Replace('_', '/');
        byte[] raw = Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
        if (raw.Length > maxBytes) { CryptographicOperations.ZeroMemory(raw); throw new FormatException(); }
        return raw;
    }
    public void Dispose() { if (!disposed) { disposed = true; ecdh.Dispose(); } }
}
