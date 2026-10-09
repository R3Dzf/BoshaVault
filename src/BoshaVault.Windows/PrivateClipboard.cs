using System.Runtime.InteropServices;
using System.IO;
using System.Windows;

namespace BoshaVault.Windows;

public static class PrivateClipboard
{
    private static string? owned;
    public static DateTime Expires { get; private set; }
    public static void Copy(string value)
    {
        var data = new DataObject();
        data.SetText(value, TextDataFormat.UnicodeText);
        // Windows documented opt-outs: DWORD 0 excludes history and cloud clipboard.
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]), false);
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(new byte[4]), false);
        Clipboard.SetDataObject(data, true);
        owned = value; Expires = DateTime.UtcNow.AddSeconds(20);
    }
    public static void Clear()
    {
        try { if (owned != null && Clipboard.ContainsText() && Clipboard.GetText() == owned) Clipboard.Clear(); }
        catch (COMException) { /* Another app owns the clipboard. Retry on the next timer tick. */ return; }
        owned = null;
    }
}
