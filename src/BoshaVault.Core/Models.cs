using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoshaVault.Core;

public sealed class VaultException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class VaultEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Folder { get; set; } = "Personal";
    public bool Favorite { get; set; }
    public bool Deleted { get; set; }
    public string UpdatedUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public Dictionary<string, long> Clock { get; set; } = [];
    public VaultEntry Clone() => JsonSerializer.Deserialize<VaultEntry>(JsonSerializer.Serialize(this, JsonOptions.Strict), JsonOptions.Strict)!;
}

public sealed class TrustedApp
{
    public string Package { get; set; } = "";
    public string Certificate { get; set; } = "";
    public bool Browser { get; set; }
    public List<string> EntryIds { get; set; } = [];
}

public sealed class VaultData
{
    public int Schema { get; set; } = 1;
    public string VaultId { get; set; } = "";
    public long Revision { get; set; }
    public List<VaultEntry> Entries { get; set; } = [];
    public List<TrustedApp> TrustedApps { get; set; } = [];
}

public sealed class KdfHeader
{
    public string Name { get; set; } = "argon2id";
    public int MemoryKiB { get; set; } = 65536;
    public int Iterations { get; set; } = 3;
    public int Parallelism { get; set; } = 4;
    public string Salt { get; set; } = "";
}

public sealed class CipherBox
{
    public string Nonce { get; set; } = "";
    public string Ciphertext { get; set; } = "";
    public string Tag { get; set; } = "";
}

public sealed class VaultEnvelope
{
    public string Format { get; set; } = "BoshaVault";
    public int Version { get; set; } = 1;
    public string VaultId { get; set; } = "";
    public long KeyEpoch { get; set; } = 1;
    public KdfHeader Kdf { get; set; } = new();
    public CipherBox Wrap { get; set; } = new();
    public CipherBox Payload { get; set; } = new();
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Strict = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow
    };
    public static void NoDuplicates(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in node.EnumerateObject())
            {
                if (!seen.Add(p.Name)) throw new VaultException("Duplicate JSON property.");
                NoDuplicates(p.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) NoDuplicates(item);
    }
}
