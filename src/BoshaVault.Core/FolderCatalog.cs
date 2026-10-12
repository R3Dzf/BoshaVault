namespace BoshaVault.Core;

public static class FolderCatalog
{
    public static readonly string[] Defaults = ["Personal", "Work", "Email", "Finance", "Social", "Shopping", "Gaming", "Other"];
    public const int MaxFolders = 10008;
    private static readonly char[] TrimCharacters = Enumerable.Range(0, 33).Select(n => (char)n).ToArray();
    public static bool Empty(string name) => name.Trim(TrimCharacters).Length == 0;
    public static string Normalize(string name)
    {
        name = name.Trim(TrimCharacters);
        if (name.Length is < 1 or > 100 || name.Any(char.IsControl))
            throw new VaultException("Use a folder name of 1–100 characters without control characters.");
        return name;
    }
    public static List<string> Choices(VaultData data) => Defaults.Concat(data.Folders)
        .Concat(data.Entries.Where(e => !e.Purged && e.Folder.Length > 0).Select(e => e.Folder))
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
    public static void Add(VaultData data, string name)
    {
        name = Normalize(name);
        if (data.Folders.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
        if (data.Folders.Count >= MaxFolders) throw new VaultException("Folder limit reached.");
        data.Folders.Add(name);
    }
    public static void Upgrade(VaultData data)
    {
        data.Schema = 2;
        foreach (string name in Defaults) Add(data, name);
        foreach (var entry in data.Entries.Where(e => !e.Purged))
        {
            entry.Folder = Empty(entry.Folder) ? "Personal" : Normalize(entry.Folder);
            Add(data, entry.Folder);
        }
    }
}
