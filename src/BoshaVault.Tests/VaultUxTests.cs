using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoshaVault.Core;
using BoshaVault.Windows;

public static class VaultUxTests
{
    public const string Master = "synthetic UX master passphrase 2026";
    private const string DevA = "11111111-1111-4111-8111-111111111111", DevB = "22222222-2222-4222-8222-222222222222";
    private static int passed;
    private static void Check(bool value, string name)
    { if (!value) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); passed++; }
    private static void Reject(Action action, string name)
    { bool refused = false; try { action(); } catch (VaultException) { refused = true; } Check(refused, name); }

    public static async Task Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "bv-ux-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        try
        {
            Check(new PasswordOptions().Length == 24, "generator default is 24 characters");
            var custom = new PasswordOptions { Length = 18, Lowercase = false, AllowedSymbols = "@#", MinimumDigits = 5, MinimumSymbols = 3 };
            Check(Enumerable.Range(0, 500).Select(_ => PasswordGenerator.Generate(custom)).All(p => p.Length == 18 &&
                p.All(c => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789@#".Contains(c)) && p.Count(char.IsDigit) >= 5 && p.Count(c => c is '@' or '#') >= 3),
                "generator enforces website alphabet, disabled groups and minimum counts");
            Check(PasswordGenerator.Generate(new PasswordOptions { Length = 8, Lowercase = false, Uppercase = false, Symbols = false }).All(c => c is >= '2' and <= '9'),
                "numeric-only short website password uses selected alphabet");
            Reject(() => PasswordGenerator.Generate(new PasswordOptions { Lowercase = false, Uppercase = false, Digits = false, Symbols = false }), "generator rejects an empty alphabet");
            Reject(() => PasswordGenerator.Generate(new PasswordOptions { Length = 8, MinimumDigits = 7, MinimumSymbols = 2 }), "generator rejects impossible required counts");
            Reject(() => PasswordGenerator.Generate(new PasswordOptions { AllowedSymbols = " @" }), "generator rejects spaces in allowed symbols");
            Reject(() => PasswordGenerator.Generate(new PasswordOptions { Symbols = true, AllowedSymbols = "" }), "generator rejects enabled empty symbols");
            Reject(() => PasswordGenerator.Generate(new PasswordOptions { Length = 129 }), "generator enforces its upper length bound");
            var preferences = new DesktopPreferences { AutoLockMinutes = 5, Generator = custom };
            preferences.Save(folder + "/prefs.json");
            Check(DesktopPreferences.Load(folder + "/prefs.json").Generator.MinimumSymbols == 3, "desktop generator defaults persist with website rules");
            File.WriteAllText(folder + "/prefs.json", "{\"Version\":1,\"AutoLockMinutes\":5,\"TrayTipShown\":true,\"BrowserAutofillEnabled\":false}");
            var oldPrefs = DesktopPreferences.Load(folder + "/prefs.json");
            Check(oldPrefs.AutoLockMinutes == 5 && !oldPrefs.BrowserAutofillEnabled && oldPrefs.Generator.Length == 24,
                "older preferences retain unrelated settings and receive generator defaults");
            using var a = await VaultSession.Create(folder + "/a", Master, DevA);
            var reviewed = new VaultEntry { Title = "Synthetic weak login", Username = "synthetic@example.invalid", Password = "password123", Folder = "Projects" };
            a.Upsert(reviewed); a.AddFolder("Empty custom folder");
            Check(FolderCatalog.Choices(a.Data).Contains("Work") && FolderCatalog.Choices(a.Data).Contains("Projects") && a.Data.Folders.Contains("Empty custom folder"),
                "preset, assigned and empty custom folders are available");
            a.AddFolder("projects"); Check(a.Data.Folders.Count(f => f.Equals("Projects", StringComparison.OrdinalIgnoreCase)) == 1, "folder creation avoids case-insensitive duplicates");
            Reject(() => a.AddFolder(" \n"), "folder creation rejects an empty name");
            a.AcknowledgeReview(reviewed.Id); Check(PasswordHealth.Audit(a.Data.Entries).AtRisk == 0, "confirming review removes only current findings");
            var changed = a.Data.Entries.Single().Clone(); changed.Title = "Renamed reviewed login"; a.Upsert(changed);
            Check(PasswordHealth.Audit(a.Data.Entries).AtRisk == 0, "renaming a reviewed login preserves its acknowledgement");
            changed = a.Data.Entries.Single().Clone(); changed.Password = "short-new"; a.Upsert(changed);
            Check(PasswordHealth.Audit(a.Data.Entries).AtRisk == 1, "changing the password brings review back"); a.AcknowledgeReview(reviewed.Id);
            var reused = new VaultEntry { Title = "Synthetic duplicate", Password = "short-new" }; a.Upsert(reused);
            Check(PasswordHealth.Audit(a.Data.Entries).AtRisk == 2, "new password reuse invalidates the previously accepted risk"); a.AcknowledgeReview(reviewed.Id);
            var moreReuse = new VaultEntry { Title = "Another synthetic duplicate", Password = "short-new" }; a.Upsert(moreReuse);
            Check(PasswordHealth.Audit(a.Data.Entries).Findings.Any(f => f.EntryId == reviewed.Id), "a changed reuse group returns a reviewed login to review");
            var doomed = new VaultEntry { Title = "Synthetic deleted credential", Username = "delete@example.invalid", Password = "Only-a-synthetic-deletion-secret!", Notes = "Synthetic private note", Folder = "Projects", Favorite = true };
            a.Upsert(doomed); byte[] stale = a.ExportEncrypted(); File.Copy(folder + "/a", folder + "/b");
            using var b = await VaultSession.Open(folder + "/b", Master, DevB);
            var offlineEdit = b.Data.Entries.Single(e => e.Id == doomed.Id).Clone(); offlineEdit.Password = "Synthetic concurrent edit 123!"; b.Upsert(offlineEdit);
            Reject(() => a.DeletePermanently(doomed.Id), "permanent deletion requires Trash first");
            var trash = a.Data.Entries.Single(e => e.Id == doomed.Id).Clone(); trash.Deleted = true; a.Upsert(trash); a.DeletePermanently(doomed.Id);
            var tombstone = a.Data.Entries.Single(e => e.Id == doomed.Id);
            Check(tombstone.Purged && tombstone.Deleted && tombstone.Password.Length == 0 && tombstone.Username.Length == 0 && tombstone.Notes.Length == 0 && tombstone.Folder.Length == 0,
                "permanent deletion removes every credential field while retaining sync identity");
            Check(!File.Exists(folder + "/a.previous"), "permanent deletion removes the previous automatic local snapshot");
            Reject(() => a.Upsert(doomed), "saved stale entry cannot restore a permanently deleted login");
            Check(a.MergeEncrypted(stale) == 0 && a.Data.Entries.Single(e => e.Id == doomed.Id).Purged,
                "stale backup merge cannot resurrect permanently deleted credentials");
            int before = a.Data.Entries.Count; Check(a.MergeEncrypted(b.ExportEncrypted()) == 0 && a.Data.Entries.Count == before && a.Data.Entries.Single(e => e.Id == doomed.Id).Purged,
                "concurrent offline edit cannot create a credential-bearing conflict after permanent deletion");
            b.MergeEncrypted(a.ExportEncrypted()); Check(b.Data.Entries.Single(e => e.Id == doomed.Id).Purged && !File.Exists(folder + "/b.previous"),
                "remote permanent deletion converges and removes that device's previous snapshot");
            var invalid = tombstone.Clone(); invalid.Password = "invalid retained secret";
            Reject(() => VaultCodec.ValidateData(new VaultData { VaultId = a.Data.VaultId, Entries = [invalid] }, a.Data.VaultId), "validation rejects tombstones that retain credential content");
            byte[] legacy = await Legacy(a.ExportEncrypted()); File.WriteAllBytes(folder + "/legacy", legacy);
            using var old = await VaultSession.Open(folder + "/legacy", Master, DevB);
            Check(old.Data.Schema == 2 && old.Data.Entries.All(e => !e.Purged && e.ReviewAcknowledgement.Length == 0) && old.Data.Folders.Contains("Personal"),
                "legacy schema 1 vault upgrades without losing active entries");
            old.AddFolder("New after upgrade"); using var reopened = await VaultSession.Open(folder + "/legacy", Master, DevA);
            Check(reopened.Data.Folders.Contains("New after upgrade"), "upgraded vault saves and reopens with new folder catalog");
            Console.WriteLine($"RESULT: {passed} vault UX / migration / review / deletion / generator checks passed.");
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task<byte[]> Legacy(byte[] input)
    {
        var env = VaultCodec.Parse(input); byte[] kek = await VaultCodec.DeriveKey(Master, env.Kdf);
        byte[] key = VaultCodec.Decrypt(kek, env.Wrap, VaultCodec.WrapAad(env)); CryptographicOperations.ZeroMemory(kek);
        byte[] plain = VaultCodec.Decrypt(key, env.Payload, VaultCodec.PayloadAad(env));
        try
        {
            var body = JsonNode.Parse(plain)!.AsObject(); body["schema"] = 1; body.Remove("folders");
            var entries = body["entries"]!.AsArray();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i]!.AsObject(); if (entry["purged"]!.GetValue<bool>()) { entries.RemoveAt(i); continue; }
                entry.Remove("purged"); entry.Remove("reviewAcknowledgement");
            }
            byte[] legacyBody = Encoding.UTF8.GetBytes(body.ToJsonString());
            try { env.Payload = VaultCodec.Encrypt(key, legacyBody, VaultCodec.PayloadAad(env)); return JsonSerializer.SerializeToUtf8Bytes(env, JsonOptions.Strict); }
            finally { CryptographicOperations.ZeroMemory(legacyBody); }
        }
        finally { CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(key); }
    }

    public static async Task Fixtures(string folder)
    {
        Directory.CreateDirectory(folder); string path = folder + "/ux-vault.boshavault";
        if (File.Exists(path)) File.Delete(path);
        using var vault = await VaultSession.Create(path, Master, DevA);
        var reviewed = new VaultEntry { Title = "Synthetic reviewed login", Username = "review@example.invalid", Password = "password123", Folder = "مشاريع" };
        var doomed = new VaultEntry { Title = "Synthetic tombstone", Password = "Synthetic old credential 42!", Username = "delete@example.invalid" };
        vault.Upsert(reviewed); vault.Upsert(doomed); vault.AddFolder("Empty custom folder");
        File.WriteAllBytes(folder + "/ux-stale.boshavault", vault.ExportEncrypted());
        File.WriteAllBytes(folder + "/ux-legacy.boshavault", await Legacy(vault.ExportEncrypted()));
        vault.AcknowledgeReview(reviewed.Id); var trash = vault.Data.Entries.Single(e => e.Id == doomed.Id).Clone(); trash.Deleted = true; vault.Upsert(trash); vault.DeletePermanently(doomed.Id);
        File.WriteAllText(folder + "/ux-state.json", JsonSerializer.Serialize(new { reviewedId = reviewed.Id, purgedId = doomed.Id }));
        Console.WriteLine("Synthetic schema 1 / schema 2 UX compatibility fixtures created.");
    }
    public static async Task Verify(string folder)
    {
        using var vault = await VaultSession.Open(folder + "/ux-java.boshavault", Master, DevA);
        Check(vault.Data.Entries.Count(e => e.Purged) == 1 && vault.Data.Entries.Where(e => e.Purged).All(e => e.Password.Length == 0), "Java permanent deletion reopens safely in .NET");
        Check(PasswordHealth.Audit(vault.Data.Entries).AtRisk == 0, "Java review acknowledgement is recognized by .NET");
        Check(vault.Data.Folders.Contains("Java custom folder") && vault.Data.Folders.Contains("مشاريع"), "Java folder catalog and Unicode survive .NET import");
        Console.WriteLine($"RESULT: {passed} reverse UX compatibility checks passed.");
    }
}
