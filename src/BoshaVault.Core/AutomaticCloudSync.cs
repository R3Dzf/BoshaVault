using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using static BoshaVault.Core.SupabaseSnapshotTransport;

namespace BoshaVault.Core;

/// <summary>Serialized encrypted sync; network I/O never owns an unlocked vault key.</summary>
public static class AutomaticCloudSync
{
    public sealed record Result(bool Uploaded, bool Downloaded, int Conflicts, bool WaitingForUnlock);
    private sealed record CacheRow(Guid DeviceSlot, byte[] Blob);
    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Stamp(IEnumerable<RemoteSnapshotMetadata> rows, Guid own) => Digest(Encoding.UTF8.GetBytes(string.Join("\n", rows.Where(x => x.DeviceSlot != own).OrderBy(x => x.DeviceSlot).Select(x => x.DeviceSlot.ToString("D") + ":" + x.Digest))));
    public static byte[] EncodePending(IReadOnlyList<RemoteEncryptedSnapshot> rows) => JsonSerializer.SerializeToUtf8Bytes(rows.Select(x => new CacheRow(x.DeviceSlot, x.Blob)));
    public static IReadOnlyList<RemoteEncryptedSnapshot> DecodePending(byte[] bytes)
    {
        if (bytes.Length > 16_000_000) throw new VaultException("Pending cloud data is too large.");
        using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 }); JsonOptions.NoDuplicates(doc.RootElement);
        var rows = JsonSerializer.Deserialize<List<CacheRow>>(bytes) ?? throw new VaultException("Invalid pending cloud data.");
        var ids = new HashSet<Guid>();
        if (rows.Count > 64 || rows.Any(x => x.DeviceSlot == Guid.Empty || !ids.Add(x.DeviceSlot) || x.Blob == null || x.Blob.Length is < 129 or > 2_000_029)) throw new VaultException("Invalid pending cloud data.");
        return rows.Select(x => new RemoteEncryptedSnapshot(x.DeviceSlot, x.Blob)).ToList();
    }
    public static IReadOnlyList<byte[]> Authenticate(IReadOnlyList<RemoteEncryptedSnapshot> rows, CloudProfile profile)
    {
        byte[] key = CloudSnapshotCodec.ParseRecoveryKey(profile.RecoveryKey); var inner = new List<byte[]>();
        try
        {
            foreach (var row in rows)
            {
                byte[] bytes = CloudSnapshotCodec.Open(row.Blob, key, profile.VaultSlot, row.DeviceSlot);
                inner.Add(bytes); var env = VaultCodec.Parse(bytes);
                if (Guid.Parse(env.VaultId) != profile.VaultId || (profile.KeyEpoch != 0 && env.KeyEpoch != profile.KeyEpoch)) throw new VaultException("A device changed the vault key. Open Sync to reconnect the vault; local data is preserved.");
            }
            return inner;
        }
        catch { foreach (byte[] bytes in inner) CryptographicOperations.ZeroMemory(bytes); throw; }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static async Task<Result> RunAsync(SupabaseSnapshotTransport transport, CloudProfile profile,
        Func<IReadOnlyList<byte[]>, Task<(byte[] Encrypted, int Conflicts)>>? merge,
        Func<Task<byte[]>> export, Func<byte[]?> readPending, Action<byte[]?> writePending,
        Action save, CancellationToken ct = default)
    {
        profile.Validate(profile.VaultId); ct.ThrowIfCancellationRequested();
        byte[]? local = null; var remote = new List<RemoteEncryptedSnapshot>();
        bool downloaded = false, uploaded = false; int conflicts = 0; long transferBytes = 0;
        try
        {
            CloudSyncStatus status = await transport.GetCloudSyncStatusAsync(ct);
            if (status.Suspended)
                throw new VaultException("Cloud sync is paused. Offline vault data and encrypted cloud snapshots are preserved.");
            local = await export(); var env = VaultCodec.Parse(local);
            if (Guid.Parse(env.VaultId) != profile.VaultId) throw new VaultException("Cloud settings do not match this vault.");
            if (profile.KeyEpoch == 0) profile.KeyEpoch = env.KeyEpoch;
            if (env.KeyEpoch != profile.KeyEpoch) throw new VaultException("The master passphrase changed. Reconnect cloud after reconciling your devices.");
            var metadata = await transport.ListMetadataAsync(profile.VaultSlot, ct); string stamp = Stamp(metadata, profile.DeviceSlot);
            byte[]? cached = readPending();
            try
            {
                bool missing = profile.DownloadedStamp != profile.MergedStamp && cached == null;
                if (stamp != profile.DownloadedStamp || missing)
                {
                    var all = await transport.ListAsync(profile.VaultSlot, ct);
                    foreach (var row in all)
                    {
                        transferBytes = Math.Min(67_108_864L,transferBytes+row.Blob.Length);
                        if (row.DeviceSlot != profile.DeviceSlot) remote.Add(row);
                        else CryptographicOperations.ZeroMemory(row.Blob);
                    }
                    var inner = Authenticate(remote, profile);
                    foreach (byte[] bytes in inner) CryptographicOperations.ZeroMemory(bytes);
                    stamp = Stamp(remote.Select(x => new RemoteSnapshotMetadata(x.DeviceSlot, Digest(x.Blob))), profile.DeviceSlot);
                    byte[] batch = EncodePending(remote);
                    try { ct.ThrowIfCancellationRequested(); writePending(batch); } finally { CryptographicOperations.ZeroMemory(batch); }
                    profile.DownloadedStamp = stamp; save(); downloaded = true;
                }
                else if (cached != null && profile.DownloadedStamp != profile.MergedStamp) remote.AddRange(DecodePending(cached));
            }
            finally { if (cached != null) CryptographicOperations.ZeroMemory(cached); }
            if (merge != null && profile.DownloadedStamp != profile.MergedStamp)
            {
                var inner = Authenticate(remote, profile);
                try
                {
                    ct.ThrowIfCancellationRequested(); var next = await merge(inner); CryptographicOperations.ZeroMemory(local); local = next.Encrypted; conflicts = next.Conflicts;
                    profile.MergedStamp = profile.DownloadedStamp; save(); writePending(null);
                }
                finally { foreach (byte[] bytes in inner) CryptographicOperations.ZeroMemory(bytes); }
            }
            var own = metadata.FirstOrDefault(x => x.DeviceSlot == profile.DeviceSlot);
            string hash = Digest(local);
            if (hash != profile.LastUploadedHash || own == null || own.Digest != profile.LastOuterHash)
            {
                byte[] key = CloudSnapshotCodec.ParseRecoveryKey(profile.RecoveryKey), outer = [];
                try
                {
                    outer = CloudSnapshotCodec.Seal(local, key, profile.VaultSlot, profile.DeviceSlot);
                    transferBytes = Math.Min(67_108_864L,transferBytes+outer.Length);
                    await transport.UploadAsync(profile.OwnerId, profile.VaultSlot, profile.DeviceSlot, outer, ct);
                    ct.ThrowIfCancellationRequested(); profile.LastUploadedHash = hash; profile.LastOuterHash = Digest(outer); uploaded = true;
                }
                finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(outer); }
            }
            if (profile.DeviceInventoryConsent)
            {
                try
                {
                    string appVersion = typeof(CloudProject).Assembly.GetName().Version?.ToString() ?? "unknown";
                    await transport.RegisterCloudDeviceAsync("windows", Environment.OSVersion.VersionString,
                        appVersion, "Windows app session", ct);
                    await transport.RecordCloudSyncOutcomeAsync("success",
                        Math.Min(67_108_864L,transferBytes),null,ct);
                    profile.DeviceInventoryState = "visible";
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is VaultException or System.Net.Http.HttpRequestException or System.IO.IOException)
                {
                    // Snapshot sync succeeded. Keep its result and retry this optional metadata update later.
                    profile.DeviceInventoryState = "pending";
                }
                save();
            }
            if (uploaded || downloaded || profile.LastSyncUtc.Length == 0 || !DateTimeOffset.TryParse(profile.LastSyncUtc, out var last) || DateTimeOffset.UtcNow - last > TimeSpan.FromMinutes(1)) { profile.LastSyncUtc = DateTimeOffset.UtcNow.ToString("O"); save(); }
            return new(uploaded, downloaded, conflicts, profile.DownloadedStamp != profile.MergedStamp);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            if (profile.DeviceInventoryConsent)
            {
                try
                {
                    string appVersion = typeof(CloudProject).Assembly.GetName().Version?.ToString() ?? "unknown";
                    await transport.RegisterCloudDeviceAsync("windows", Environment.OSVersion.VersionString,
                        appVersion, "Windows app session", ct);
                    await transport.RecordCloudSyncOutcomeAsync("failure",
                        Math.Min(67_108_864L,transferBytes),BoundedSyncErrorCode(ex),ct);
                }
                catch (Exception telemetryError) when (telemetryError is VaultException or System.Net.Http.HttpRequestException or System.IO.IOException or OperationCanceledException)
                {
                    // Never replace the sync failure with a best-effort metadata failure.
                }
            }
            throw;
        }
        finally { if (local != null) CryptographicOperations.ZeroMemory(local); foreach (var row in remote) CryptographicOperations.ZeroMemory(row.Blob); }
    }

    private static string BoundedSyncErrorCode(Exception ex)
    {
        string message=ex.Message;
        if(message.Contains("quota",StringComparison.OrdinalIgnoreCase))return "quota_exceeded";
        if(message.Contains("paused",StringComparison.OrdinalIgnoreCase) ||
           message.Contains("suspended",StringComparison.OrdinalIgnoreCase))return "sync_suspended";
        if(message.Contains("sign-in",StringComparison.OrdinalIgnoreCase) ||
           message.Contains("session",StringComparison.OrdinalIgnoreCase) ||
           message.Contains("auth",StringComparison.OrdinalIgnoreCase))return "auth";
        if(message.Contains("network",StringComparison.OrdinalIgnoreCase) ||
           message.Contains("reached",StringComparison.OrdinalIgnoreCase) ||
           ex is System.Net.Http.HttpRequestException or System.IO.IOException)return "network";
        return "transport";
    }
}
