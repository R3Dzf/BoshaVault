using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using BoshaVault.Core;

namespace BoshaVault.Windows;

/// <summary>DPAPI CurrentUser, with no plaintext refresh token or recovery key on disk.</summary>
internal sealed class CloudProfileStore(string root)
{
    private string Path => System.IO.Path.Combine(root, "cloud-profile.dpapi");
    public bool Exists => File.Exists(Path);
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        if (bytes.Length is < 1 or > 65536) throw new VaultException("Invalid protected cloud settings.");
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = protect
                ? CryptProtectData(ref input, "BoshaVault cloud settings", 0, 0, 0, 1, out output)
                : CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out output);
            if (!ok || output.Size is < 1 or > 65536) throw new VaultException("Windows could not open protected cloud settings. Reconnect this device.");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Marshal.Copy(new byte[input.Size], 0, input.Data, input.Size);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != 0)
            {
                if (output.Size is > 0 and <= 65536) Marshal.Copy(new byte[output.Size], 0, output.Data, output.Size);
                LocalFree(output.Data);
            }
        }
    }

    public CloudProfile? Load(Guid vaultId)
    {
        if (!Exists) return null;
        if (new FileInfo(Path).Length > 65536) throw new VaultException("Protected cloud settings are too large.");
        byte[] data = Transform(File.ReadAllBytes(Path), false);
        try
        {
            var result = JsonSerializer.Deserialize<CloudProfile>(data, CloudPairing.Json) ?? throw new VaultException("Invalid protected cloud settings.");
            result.Validate(vaultId);
            return result;
        }
        catch (JsonException) { throw new VaultException("Protected cloud settings are invalid. Reconnect this device."); }
        finally { CryptographicOperations.ZeroMemory(data); }
    }

    public void Save(CloudProfile profile)
    {
        profile.Validate(profile.VaultId);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(profile, CloudPairing.Json);
        byte[] encrypted;
        try { encrypted = Transform(plain, true); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        string temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, Path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); CryptographicOperations.ZeroMemory(encrypted); }
    }

    public void Forget() { if (Exists) File.Delete(Path); }
}
