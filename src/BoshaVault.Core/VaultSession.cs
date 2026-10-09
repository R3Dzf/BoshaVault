using System.Security.Cryptography;
using System.Text.Json;

namespace BoshaVault.Core;

public sealed class VaultSession : IDisposable
{
    private byte[]? key;
    private byte[] fileHash;
    private VaultEnvelope envelope;
    public VaultData Data { get; private set; }
    public string DeviceId { get; }
    public string Path { get; }
    public bool IsOpen => key != null;

    private VaultSession(string path, string deviceId, VaultEnvelope env, VaultData data, byte[] secret, byte[] hash)
    { Path = path; DeviceId = deviceId; envelope = env; Data = data; key = secret; fileHash = hash; }

    public static async Task<VaultSession> Create(string path, string password, string deviceId)
    {
        VaultCodec.ValidateNewPassword(password);
        if (!VaultCodec.IsId(deviceId)) throw new VaultException("Invalid device ID.");
        if (File.Exists(path)) throw new VaultException("A vault already exists.");
        var env = new VaultEnvelope { VaultId = Guid.NewGuid().ToString(), Kdf = new() { Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)) } };
        var secret = RandomNumberGenerator.GetBytes(32);
        byte[] kek = await VaultCodec.DeriveKey(password, env.Kdf);
        try { env.Wrap = VaultCodec.Encrypt(kek, secret, VaultCodec.WrapAad(env)); }
        finally { CryptographicOperations.ZeroMemory(kek); }
        var data = new VaultData { VaultId = env.VaultId };
        var bytes = VaultCodec.Seal(env, data, secret);
        try { AtomicWrite(path, bytes, null, false); }
        catch { CryptographicOperations.ZeroMemory(secret); throw; }
        return new(path, deviceId, env, data, secret, SHA256.HashData(bytes));
    }

    public static async Task<VaultSession> Open(string path, string password, string deviceId)
    {
        byte[] bytes = ReadBounded(path);
        var env = VaultCodec.Parse(bytes);
        byte[] kek = await VaultCodec.DeriveKey(password, env.Kdf);
        byte[]? secret = null;
        try
        {
            secret = VaultCodec.Decrypt(kek, env.Wrap, VaultCodec.WrapAad(env));
            var data = VaultCodec.OpenBody(env, secret);
            return new(path, deviceId, env, data, secret, SHA256.HashData(bytes));
        }
        catch { if (secret != null) CryptographicOperations.ZeroMemory(secret); throw; }
        finally { CryptographicOperations.ZeroMemory(kek); }
    }

    public void Upsert(VaultEntry entry)
    {
        EnsureOpen();
        var candidate = CloneData();
        var old = candidate.Entries.FirstOrDefault(e => e.Id == entry.Id);
        var copy = entry.Clone();
        copy.Clock = old == null ? [] : new(old.Clock);
        MergeEngine.Tick(copy.Clock, DeviceId);
        copy.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O");
        if (old != null) candidate.Entries.Remove(old);
        candidate.Entries.Add(copy); candidate.Revision++;
        Commit(candidate);
    }

    /// <summary>Imports validated entries using one atomic encrypted update, never a partial CSV import.</summary>
    public int ImportEntries(IReadOnlyCollection<VaultEntry> incoming)
    {
        EnsureOpen();
        if (incoming.Count == 0) return 0;
        if (incoming.Count > CredentialCsvImport.MaxRows ||
            (long)Data.Entries.Count + incoming.Count > VaultCodec.MaxEntries)
            throw new VaultException("Import exceeds the vault entry limit.");
        var candidate = CloneData();
        var existingIds = candidate.Entries.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in incoming)
        {
            if (!VaultCodec.IsId(entry.Id) || !existingIds.Add(entry.Id) ||
                entry.Deleted || entry.Username.Length > 2000 || entry.Password.Length is 0 or > 4096)
                throw new VaultException("Invalid import item; no entries were saved.");
            var copy = entry.Clone();
            copy.Clock = [];
            MergeEngine.Tick(copy.Clock, DeviceId);
            copy.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O");
            candidate.Entries.Add(copy);
        }
        candidate.Revision++;
        Commit(candidate);
        return incoming.Count;
    }

    public int MergeEncrypted(byte[] bytes)
    {
        EnsureOpen();
        var remoteEnv = VaultCodec.Parse(bytes);
        if (remoteEnv.VaultId != envelope.VaultId) throw new VaultException("This file belongs to a different vault.");
        if (remoteEnv.KeyEpoch != envelope.KeyEpoch) throw new VaultException("This backup uses a rotated vault key. Merge it using its master passphrase.");
        var remote = VaultCodec.OpenBody(remoteEnv, key!);
        var candidate = CloneData();
        int conflicts = MergeEngine.Merge(candidate, remote, DeviceId);
        Commit(candidate);
        ClearData(remote);
        return conflicts;
    }

    public bool NeedsPassphrase(byte[] bytes) { EnsureOpen(); return VaultCodec.Parse(bytes).KeyEpoch != envelope.KeyEpoch; }
    public async Task<int> MergeUsingPassword(byte[] bytes, string incomingPassword)
    {
        EnsureOpen();
        var remoteEnv = VaultCodec.Parse(bytes);
        if (remoteEnv.VaultId != envelope.VaultId) throw new VaultException("This file belongs to a different vault.");
        byte[] kek = await VaultCodec.DeriveKey(incomingPassword, remoteEnv.Kdf).ConfigureAwait(false);
        byte[]? remoteKey = null;
        try
        {
            remoteKey = VaultCodec.Decrypt(kek, remoteEnv.Wrap, VaultCodec.WrapAad(remoteEnv));
            var remote = VaultCodec.OpenBody(remoteEnv, remoteKey); EnsureOpen();
            if (remoteEnv.KeyEpoch == envelope.KeyEpoch && !CryptographicOperations.FixedTimeEquals(remoteKey, key!))
                throw new VaultException("Both devices rotated their key independently. Export both backups and recover them separately.");
            var candidate = CloneData(); int conflicts = MergeEngine.Merge(candidate, remote, DeviceId);
            if (remoteEnv.KeyEpoch > envelope.KeyEpoch)
            {
                byte[] merged = VaultCodec.Seal(remoteEnv, candidate, remoteKey);
                AtomicWrite(Path, merged, fileHash, false);
                if (File.Exists(Path + ".previous")) File.Delete(Path + ".previous");
                CryptographicOperations.ZeroMemory(key!); key = remoteKey; remoteKey = null;
                ClearData(Data); Data = candidate; envelope = remoteEnv; fileHash = SHA256.HashData(merged);
            }
            else Commit(candidate);
            ClearData(remote); return conflicts;
        }
        finally { CryptographicOperations.ZeroMemory(kek); if (remoteKey != null) CryptographicOperations.ZeroMemory(remoteKey); }
    }

    public async Task ChangePassword(string current, string next)
    {
        EnsureOpen(); VaultCodec.ValidateNewPassword(next);
        // The caller must reauthenticate with the CURRENT passphrase.
        byte[] currentKek = await VaultCodec.DeriveKey(current, envelope.Kdf);
        byte[]? checkedKey = null;
        try { checkedKey = VaultCodec.Decrypt(currentKek, envelope.Wrap, VaultCodec.WrapAad(envelope)); if (!CryptographicOperations.FixedTimeEquals(key!, checkedKey)) throw new VaultException("Incorrect current passphrase."); }
        finally { CryptographicOperations.ZeroMemory(currentKek); if (checkedKey != null) CryptographicOperations.ZeroMemory(checkedKey); }
        var env = JsonSerializer.Deserialize<VaultEnvelope>(JsonSerializer.Serialize(envelope, JsonOptions.Strict), JsonOptions.Strict)!;
        env.KeyEpoch = checked(env.KeyEpoch + 1);
        env.Kdf.Salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        byte[] nextKey = RandomNumberGenerator.GetBytes(32);
        byte[] kek = await VaultCodec.DeriveKey(next, env.Kdf);
        try
        {
            EnsureOpen(); env.Wrap = VaultCodec.Encrypt(kek, nextKey, VaultCodec.WrapAad(env));
            var candidate = CloneData(); candidate.Revision++;
            var bytes = VaultCodec.Seal(env, candidate, nextKey);
            AtomicWrite(Path, bytes, fileHash, false);
            if (File.Exists(Path + ".previous")) File.Delete(Path + ".previous");
            CryptographicOperations.ZeroMemory(key!); key = nextKey; nextKey = [];
            ClearData(Data); envelope = env; fileHash = SHA256.HashData(bytes); Data = candidate;
        }
        finally { CryptographicOperations.ZeroMemory(kek); CryptographicOperations.ZeroMemory(nextKey); }
    }

    public byte[] ExportEncrypted()
    {
        EnsureOpen(); var bytes = ReadBounded(Path);
        if (!CryptographicOperations.FixedTimeEquals(fileHash, SHA256.HashData(bytes))) throw new VaultException("The vault changed outside this session. Lock and reopen it.");
        return bytes;
    }
    private VaultData CloneData() => JsonSerializer.Deserialize<VaultData>(JsonSerializer.Serialize(Data, JsonOptions.Strict), JsonOptions.Strict)!;
    private void Commit(VaultData candidate)
    {
        var bytes = VaultCodec.Seal(envelope, candidate, key!);
        AtomicWrite(Path, bytes, fileHash, true);
        var previousData = Data; Data = candidate; fileHash = SHA256.HashData(bytes); ClearData(previousData);
    }
    private void EnsureOpen() { if (key == null) throw new VaultException("Vault is locked."); }

    public static byte[] ReadBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 100 or > VaultCodec.MaxFileBytes) throw new VaultException("Invalid vault file size.");
        var bytes = new byte[(int)stream.Length]; stream.ReadExactly(bytes); return bytes;
    }

    public static void AtomicWrite(string path, byte[] bytes, byte[]? expectedHash, bool previous)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        // A separate lock file serializes writers; no secrets are stored in it.
        using var writeLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (expectedHash == null && File.Exists(path)) throw new VaultException("Refusing to replace an existing vault.");
        if (expectedHash != null && (!File.Exists(path) || !CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(ReadBounded(path)))))
            throw new VaultException("Another session changed this vault. Lock and reopen it.");
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                stream.Write(bytes); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, previous ? path + ".previous" : null);
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void ClearData(VaultData data)
    { foreach (var e in data.Entries) { e.Password = ""; e.Username = ""; e.Notes = ""; e.Title = ""; e.Url = ""; } data.Entries.Clear(); data.TrustedApps.Clear(); }
    public void Dispose()
    { if (key != null) { CryptographicOperations.ZeroMemory(key); key = null; } ClearData(Data); }
}
