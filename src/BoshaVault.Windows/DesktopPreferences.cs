using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BoshaVault.Core;

namespace BoshaVault.Windows;

public sealed class DesktopPreferences
{
    public int Version { get; set; } = 1;
    public int AutoLockMinutes { get; set; } = 2;
    public bool TrayTipShown { get; set; }
    public bool BrowserAutofillEnabled { get; set; } = true;
    public PasswordOptions Generator { get; set; } = new();
    private static readonly JsonSerializerOptions Options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 4 };
    public static bool ValidTimeout(int minutes) => minutes is 1 or 2 or 5 or 10;

    public static DesktopPreferences Load(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 2048) return new();
            var preferences = JsonSerializer.Deserialize<DesktopPreferences>(stream, Options);
            if (preferences is not { Version: 1 } || !ValidTimeout(preferences.AutoLockMinutes)) return new();
            try { PasswordGenerator.Validate(preferences.Generator); }
            catch (Exception ex) when (ex is VaultException or NullReferenceException) { preferences.Generator = new(); }
            return preferences;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save(string path)
    {
        if (Version != 1 || !ValidTimeout(AutoLockMinutes)) throw new ArgumentException("Invalid desktop preferences.");
        PasswordGenerator.Validate(Generator);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { JsonSerializer.Serialize(stream, this, Options); stream.Flush(true); }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
