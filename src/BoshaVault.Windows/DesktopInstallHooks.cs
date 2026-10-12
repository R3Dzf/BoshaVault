using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BoshaVault.Windows;

internal static class DesktopInstallHooks
{
    // Repoint only existing, locally approved integration. No new extension origins,
    // startup preference or vault/profile files are introduced by an installer hook.
    public static void RepairExisting()
    {
        try { WindowsStartup.RepairExisting(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        string manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BoshaVault", "NativeMessaging", "com.boshavault.desktop.json");
        string binary = Path.Combine(AppContext.BaseDirectory, "Lockvyn.NativeHost.exe");
        string staged = manifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!File.Exists(manifest) || !File.Exists(binary) || new FileInfo(manifest).Length > 16384) return;
            var data = JsonNode.Parse(File.ReadAllText(manifest)) as JsonObject;
            if (data?["name"]?.GetValue<string>() != "com.boshavault.desktop" || data["type"]?.GetValue<string>() != "stdio" || data["allowed_origins"] is not JsonArray origins || origins.Count is < 1 or > 64) return;
            if (origins.Any(o => o is not JsonValue v || !v.TryGetValue<string>(out var origin) || !Regex.IsMatch(origin, "^chrome-extension://[a-p]{32}/$"))) return;
            data["path"] = binary;
            File.WriteAllText(staged, data.ToJsonString()); File.Move(staged, manifest, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { }
        finally { try { if (File.Exists(staged)) File.Delete(staged); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
    }
}
