using System.IO;
using Microsoft.Win32;

namespace BoshaVault.Windows;

internal static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", ValueName = "BoshaVault";
    private static string Command
    {
        get
        {
            string path = Environment.ProcessPath ?? throw new IOException("Cannot find the executable. Start the published Lockvyn.exe.");
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) || path.IndexOfAny(['\"', '\r', '\n']) >= 0)
                throw new IOException("Start the published Lockvyn.exe before enabling Windows startup.");
            return "\"" + path + "\" --background";
        }
    }
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true) ?? throw new IOException("Cannot update your Windows startup setting.");
        if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
    internal static void RepairExisting()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key?.GetValue(ValueName) is string value && System.Text.RegularExpressions.Regex.IsMatch(value,
            "^\"[^\"\\r\\n]+\\\\(?:BoshaVault|Lockvyn)\\.exe\" --background$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            key.SetValue(ValueName, Command, RegistryValueKind.String);
    }
}
