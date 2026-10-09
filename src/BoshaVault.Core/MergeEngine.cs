using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BoshaVault.Core;

public static class MergeEngine
{
    // Version vectors distinguish an old edit from an edit made independently offline.
    public static int Merge(VaultData local, VaultData remote, string deviceId)
    {
        if (local.VaultId != remote.VaultId) throw new VaultException("This file belongs to a different vault. Import it on the unlock screen instead.");
        var map = local.Entries.ToDictionary(x => x.Id, x => x.Clone());
        int conflicts = 0;
        foreach (var incoming in remote.Entries)
        {
            if (!map.TryGetValue(incoming.Id, out var current)) { map[incoming.Id] = incoming.Clone(); continue; }
            int relation = Compare(current.Clock, incoming.Clock);
            if (relation == 1) continue;
            if (relation == -1) { map[incoming.Id] = incoming.Clone(); continue; }
            bool identical = SameFields(current, incoming);
            if (relation == 0)
            {
                if (!identical) throw new VaultException("Conflicting content with an identical revision clock.");
                continue;
            }
            var joined = Join(current.Clock, incoming.Clock);
            Tick(joined, deviceId);
            if (identical) { current.Clock = joined; map[current.Id] = current; continue; }
            string conflictId = ConflictId(current, incoming);
            // Keep the edited version when it conflicts with a deletion; retain the tombstone too.
            var alternate = incoming.Deleted && !current.Deleted ? current.Clone() : incoming.Clone();
            var primary = incoming.Deleted && !current.Deleted ? incoming.Clone() : current.Clone();
            primary.Clock = joined;
            map[current.Id] = primary;
            alternate.Id = conflictId;
            alternate.Title = alternate.Title[..Math.Min(180, alternate.Title.Length)] + " (conflict)";
            alternate.Clock = new(joined);
            if (!map.ContainsKey(conflictId)) { map[conflictId] = alternate; conflicts++; }
        }
        if (map.Count > VaultCodec.MaxEntries) throw new VaultException("Merged vault exceeds the entry limit.");
        local.Entries = map.Values.ToList();
        // App trust is deliberately local: sync never silently grants a new app access.
        local.Revision = checked(Math.Max(local.Revision, remote.Revision) + 1);
        return conflicts;
    }
    public static void Tick(Dictionary<string, long> clock, string device)
    {
        clock[device] = checked(clock.GetValueOrDefault(device) + 1);
        if (clock.Count > 64 || clock[device] > 1_000_000_000) throw new VaultException("Device clock limit reached.");
    }
    public static int Compare(Dictionary<string, long> a, Dictionary<string, long> b)
    {
        bool greater = false, less = false;
        foreach (string k in a.Keys.Union(b.Keys))
        {
            long x = a.GetValueOrDefault(k), y = b.GetValueOrDefault(k);
            greater |= x > y; less |= x < y;
        }
        return greater && less ? 2 : greater ? 1 : less ? -1 : 0;
    }
    private static Dictionary<string, long> Join(Dictionary<string, long> a, Dictionary<string, long> b) =>
        a.Keys.Union(b.Keys).ToDictionary(k => k, k => Math.Max(a.GetValueOrDefault(k), b.GetValueOrDefault(k)));
    private static bool SameFields(VaultEntry a, VaultEntry b) =>
        a.Title == b.Title && a.Username == b.Username && a.Password == b.Password && a.Url == b.Url && a.Notes == b.Notes && a.Folder == b.Folder && a.Favorite == b.Favorite && a.Deleted == b.Deleted;
    private static string ConflictId(VaultEntry a, VaultEntry b)
    {
        string Vector(Dictionary<string, long> c) => string.Join(";", c.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}:{kv.Value}"));
        var ordered = new[] { Vector(a.Clock), Vector(b.Clock) }.Order(StringComparer.Ordinal);
        string hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(a.Id + "|" + string.Join("|", ordered))))[..32].ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
}
