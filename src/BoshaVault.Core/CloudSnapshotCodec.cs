using System.Security.Cryptography;
using System.Text;

namespace BoshaVault.Core;

/// <summary>
/// Adds independent end-to-end encryption to the already encrypted vault file.
/// Supabase sees only an opaque binary blob (plus random slot UUIDs/size/time).
/// A 256-bit recovery key is generated locally and never sent to the cloud.
/// </summary>
public static class CloudSnapshotCodec
{
    public const int MaxInnerBytes = 2_000_000;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int HeaderBytes = 1 + NonceBytes + TagBytes;
    private const byte FormatVersion = 1;

    public static string NewRecoveryKey()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        try { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_'); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static byte[] ParseRecoveryKey(string code)
    {
        if (code == null || code.Length != 43 || code.Any(c =>
            !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new VaultException("Invalid cloud recovery key.");
        try
        {
            string normal = code.Replace('-','+').Replace('_','/')+"=";
            byte[] key = Convert.FromBase64String(normal);
            if (key.Length != 32) throw new VaultException("Invalid cloud recovery key size.");
            return key;
        }
        catch (FormatException ex) { throw new VaultException("Invalid cloud recovery key.",ex); }
    }

    private static byte[] Aad(Guid vaultSlot, Guid deviceSlot)
    {
        if (vaultSlot == Guid.Empty || deviceSlot == Guid.Empty)
            throw new VaultException("Cloud slot identifiers must be random nonempty UUIDs.");
        return Encoding.UTF8.GetBytes("BoshaVault.CloudOuter.v1|" +
            vaultSlot.ToString("D") + "|" + deviceSlot.ToString("D"));
    }

    public static byte[] Seal(ReadOnlySpan<byte> innerEncryptedVault,
        ReadOnlySpan<byte> recoveryKey, Guid vaultSlot, Guid deviceSlot)
    {
        if (recoveryKey.Length != 32) throw new VaultException("Invalid cloud recovery key size.");
        if (innerEncryptedVault.Length is < 100 or > MaxInnerBytes)
            throw new VaultException("Vault exceeds the current cloud snapshot limit.");
        // Validate format locally; plaintext accounts remain inaccessible to this method.
        VaultCodec.Parse(innerEncryptedVault);
        byte[] result = new byte[checked(HeaderBytes + innerEncryptedVault.Length)];
        result[0] = FormatVersion;
        RandomNumberGenerator.Fill(result.AsSpan(1,NonceBytes));
        byte[] aad = Aad(vaultSlot,deviceSlot);
        try
        {
            using var gcm = new AesGcm(recoveryKey,TagBytes);
            gcm.Encrypt(result.AsSpan(1,NonceBytes),
                innerEncryptedVault,
                result.AsSpan(HeaderBytes),result.AsSpan(1+NonceBytes,TagBytes),aad);
            return result;
        }
        catch { CryptographicOperations.ZeroMemory(result); throw; }
        finally { CryptographicOperations.ZeroMemory(aad); }
    }

    public static byte[] Open(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> recoveryKey,
        Guid vaultSlot, Guid deviceSlot)
    {
        if (recoveryKey.Length != 32) throw new VaultException("Invalid cloud recovery key size.");
        if (blob.Length is < HeaderBytes + 100 or > HeaderBytes + MaxInnerBytes ||
            blob[0] != FormatVersion)
            throw new VaultException("Invalid cloud snapshot.");
        byte[] result = new byte[blob.Length-HeaderBytes];
        byte[] aad = Aad(vaultSlot,deviceSlot);
        try
        {
            using var gcm = new AesGcm(recoveryKey,TagBytes);
            gcm.Decrypt(blob.Slice(1,NonceBytes),blob.Slice(HeaderBytes),
                blob.Slice(1+NonceBytes,TagBytes),result,aad);
            VaultCodec.Parse(result);
            return result;
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(result);
            throw new VaultException("Cloud snapshot authentication failed. Wrong recovery key, slot, or corrupted backup.",ex);
        }
        catch { CryptographicOperations.ZeroMemory(result); throw; }
        finally { CryptographicOperations.ZeroMemory(aad); }
    }
}
