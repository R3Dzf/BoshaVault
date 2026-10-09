using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace BoshaVault.Core;

public static class VaultCodec
{
    public const int MaxFileBytes = 16 * 1024 * 1024;
    public const int MaxEntries = 10000;

    public static void ValidateNewPassword(string password)
    {
        if (password.Length < 16 || Encoding.UTF8.GetByteCount(password) > 1024)
            throw new VaultException("Use a master passphrase of at least 16 characters (maximum 1024 UTF-8 bytes).");
    }

    public static async Task<byte[]> DeriveKey(string password, KdfHeader h)
    {
        if (h.Name != "argon2id" || h.MemoryKiB != 65536 || h.Iterations != 3 || h.Parallelism != 4) throw new VaultException("Unsafe key derivation parameters.");
        if (Encoding.UTF8.GetByteCount(password) > 1024) throw new VaultException("Passphrase is too long.");
        var bytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC));
        var salt = Decode(h.Salt, 16);
        try
        {
            using var argon = new Argon2id(bytes)
            {
                Salt = salt, MemorySize = h.MemoryKiB,
                Iterations = h.Iterations, DegreeOfParallelism = h.Parallelism
            };
            return await argon.GetBytesAsync(32).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(salt); }
    }

    public static string WrapAad(VaultEnvelope e) =>
        $"BoshaVault|1|{e.VaultId}|{e.KeyEpoch}|argon2id|65536|3|4|{e.Kdf.Salt}|wrap";
    public static string PayloadAad(VaultEnvelope e) =>
        $"BoshaVault|1|{e.VaultId}|{e.KeyEpoch}|argon2id|65536|3|4|{e.Kdf.Salt}|payload|{e.Wrap.Nonce}|{e.Wrap.Ciphertext}|{e.Wrap.Tag}";

    public static CipherBox Encrypt(byte[] key, ReadOnlySpan<byte> plaintext, string aad)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(aad));
        return new() { Nonce = Convert.ToBase64String(nonce), Ciphertext = Convert.ToBase64String(ciphertext), Tag = Convert.ToBase64String(tag) };
    }

    public static byte[] Decrypt(byte[] key, CipherBox box, string aad)
    {
        var nonce = Decode(box.Nonce, 12);
        var tag = Decode(box.Tag, 16);
        var ciphertext = Decode(box.Ciphertext, null);
        var plain = new byte[ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, ciphertext, tag, plain, Encoding.UTF8.GetBytes(aad));
            return plain;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new VaultException("Incorrect passphrase or damaged / modified vault.", ex);
        }
    }

    public static VaultEnvelope Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 100 or > MaxFileBytes) throw new VaultException("Invalid vault file size.");
        try
        {
            using var doc = JsonDocument.Parse(bytes.ToArray(), new() { MaxDepth = 16 });
            JsonOptions.NoDuplicates(doc.RootElement);
            var e = JsonSerializer.Deserialize<VaultEnvelope>(bytes, JsonOptions.Strict) ?? throw new VaultException("Invalid vault.");
            if (e.Format != "BoshaVault" || e.Version != 1 || e.KeyEpoch is < 1 or > 1000000 || !doc.RootElement.TryGetProperty("keyEpoch", out _) || !IsId(e.VaultId) || e.Kdf == null || e.Wrap == null || e.Payload == null)
                throw new VaultException("Unsupported vault format.");
            if (e.Kdf.Name != "argon2id" || e.Kdf.MemoryKiB != 65536 || e.Kdf.Iterations != 3 || e.Kdf.Parallelism != 4)
                throw new VaultException("Unsupported or unsafe key derivation parameters.");
            Decode(e.Kdf.Salt, 16); Decode(e.Wrap.Nonce, 12); Decode(e.Wrap.Ciphertext, 32); Decode(e.Wrap.Tag, 16);
            Decode(e.Payload.Nonce, 12); Decode(e.Payload.Ciphertext, null); Decode(e.Payload.Tag, 16);
            return e;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or NullReferenceException)
        { throw new VaultException("Invalid vault structure.", ex); }
    }

    public static VaultData OpenBody(VaultEnvelope e, byte[] key)
    {
        var plain = Decrypt(key, e.Payload, PayloadAad(e));
        try
        {
            using var doc = JsonDocument.Parse(plain, new() { MaxDepth = 16 });
            JsonOptions.NoDuplicates(doc.RootElement);
            var data = JsonSerializer.Deserialize<VaultData>(plain, JsonOptions.Strict) ?? throw new VaultException("Invalid vault contents.");
            ValidateData(data, e.VaultId);
            return data;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException)
        { throw new VaultException("Invalid vault contents.", ex); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static void ValidateData(VaultData data, string vaultId)
    {
        if (data.Schema != 1 || data.VaultId != vaultId || data.Revision < 0 || data.Entries == null || data.Entries.Count > MaxEntries || data.TrustedApps == null || data.TrustedApps.Count > 200)
            throw new VaultException("Invalid vault contents.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in data.Entries)
        {
            if (e == null || !IsId(e.Id) || !ids.Add(e.Id) || e.Clock == null || e.Clock.Count is 0 or > 64 || !DateTimeOffset.TryParse(e.UpdatedUtc, out _))
                throw new VaultException("Invalid entry identity / revision.");
            foreach (var (device, count) in e.Clock)
                if (!IsId(device) || count is < 1 or > 1_000_000_000) throw new VaultException("Invalid entry clock.");
            if (e.Title == null || e.Title.Length is 0 or > 200 || e.Username == null || e.Username.Length > 2000 || e.Password == null || e.Password.Length > 4096 || e.Url == null || e.Url.Length > 2048 || e.Notes == null || e.Notes.Length > 32000 || e.Folder == null || e.Folder.Length > 100)
                throw new VaultException("Invalid entry fields.");
            if (e.Url.Length > 0) OriginPolicy.ExactHost(e.Url);
        }
        foreach (var app in data.TrustedApps)
            if (app == null || app.Package == null || app.Package.Length is < 3 or > 200 || app.Certificate == null || !System.Text.RegularExpressions.Regex.IsMatch(app.Certificate, "^[a-f0-9]{64}$") || app.EntryIds == null || app.EntryIds.Count > MaxEntries || app.EntryIds.Any(x => !IsId(x)))
                throw new VaultException("Invalid trusted application.");
    }

    public static bool IsId(string? id) => id != null && Guid.TryParseExact(id, "D", out _) && id == id.ToLowerInvariant();
    public static byte[] Decode(string s, int? required)
    {
        if (s == null || s.Length > MaxFileBytes) throw new VaultException("Invalid encoded field.");
        var result = Convert.FromBase64String(s);
        if (required != null && result.Length != required || Convert.ToBase64String(result) != s)
            throw new VaultException("Invalid encoded field.");
        return result;
    }

    public static byte[] Seal(VaultEnvelope envelope, VaultData data, byte[] key)
    {
        ValidateData(data, envelope.VaultId);
        var plain = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions.Strict);
        try
        {
            envelope.Payload = Encrypt(key, plain, PayloadAad(envelope));
            var result = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions.Strict);
            if (result.Length > MaxFileBytes) throw new VaultException("Vault is too large.");
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
