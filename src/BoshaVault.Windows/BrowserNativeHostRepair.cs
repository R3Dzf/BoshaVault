using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BoshaVault.Windows;

/// <summary>
/// Repairs a previously user-approved Native Messaging host after Velopack
/// replaces the app directory. Never installs a browser extension, creates
/// enterprise policies, reads browser profiles, or broadens extension access.
/// Native host registration is shared by all profiles of a browser for this
/// Windows user; each profile still controls its own extension installation.
/// </summary>
internal static class BrowserNativeHostRepair
{
    private const string HostName = "com.boshavault.desktop";
    private static readonly Regex ApprovedOrigin = new(
        @"^chrome-extension://[a-p]{32}/$", RegexOptions.CultureInvariant);

    private static readonly string[] BrowserKeys =
    [
        @"Software\Google\Chrome\NativeMessagingHosts\" + HostName,
        @"Software\Microsoft\Edge\NativeMessagingHosts\" + HostName,
        @"Software\Chromium\NativeMessagingHosts\" + HostName
    ];

    internal static string ManifestPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BoshaVault", "NativeMessaging", HostName + ".json");

    internal static bool TryRepair(string installedAppFolder)
    {
        try
        {
            // Approval is established by Install-BrowserAutofill.ps1.
            // If no browser host is registered, do nothing silently.
            string manifestPath = ManifestPath;
            if (!File.Exists(manifestPath)) return false;
            bool approved = false;
            foreach (string keyPath in BrowserKeys)
            {
                using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
                if (key == null) continue;
                if (string.Equals(key.GetValue(null) as string, manifestPath,
                    StringComparison.OrdinalIgnoreCase)) approved = true;
            }
            if (!approved) return false;

            string executable = Path.Combine(installedAppFolder, "Lockvyn.NativeHost.exe");
            if (!File.Exists(executable))
                executable = Path.Combine(installedAppFolder, "BoshaVault.NativeHost.exe");
            if (!File.Exists(executable)) return false;

            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8),
                new JsonDocumentOptions { MaxDepth = 6 });
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("name", out var name) || name.GetString() != HostName ||
                !root.TryGetProperty("type", out var type) || type.GetString() != "stdio" ||
                !root.TryGetProperty("allowed_origins", out var origins) ||
                origins.ValueKind != JsonValueKind.Array) return false;

            string[] allowed = origins.EnumerateArray()
                .Where(o => o.ValueKind == JsonValueKind.String)
                .Select(o => o.GetString() ?? "")
                .Where(s => ApprovedOrigin.IsMatch(s))
                .Distinct(StringComparer.Ordinal).ToArray();
            // Fail closed if any manifest origin is invalid; never silently
            // broaden who can talk to the vault's local native host.
            if (allowed.Length == 0 || allowed.Length != origins.GetArrayLength()) return false;
            if (root.TryGetProperty("path", out var prior) &&
                string.Equals(prior.GetString(), executable, StringComparison.OrdinalIgnoreCase))
                return true;

            string json = JsonSerializer.Serialize(new
            {
                name = HostName,
                description = "Lockvyn local approved browser Autofill",
                path = executable,
                type = "stdio",
                allowed_origins = allowed
            });
            string temporary = manifestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                File.Move(temporary, manifestPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or System.Security.SecurityException or ArgumentException
            or InvalidOperationException)
        {
            // Native host is optional; never block vault launch or disclose paths.
            return false;
        }
    }
}
