using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace BoshaVault.Core;

public sealed class BrowserAutofillRequest
{
    public string Op { get; set; } = "";
    public string Origin { get; set; } = "";
    public string EntryId { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
public sealed class BrowserAccount
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
}
public sealed class BrowserAutofillResponse
{
    public string Status { get; set; } = "error";
    public string Message { get; set; } = "";
    public List<BrowserAccount> Accounts { get; set; } = [];
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
public static class BrowserAutofillProtocol
{
    public const int MaxBytes = 65536;
    public const string HostName = "com.boshavault.desktop";

    // Match the Windows logon session, not a global service or network port.
    public static string PipeName()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string user = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No Windows user identity.");
        string identity = user + "|" + Process.GetCurrentProcess().SessionId;
        return "BoshaVault-Browser-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
    }
    public static string Validate(BrowserAutofillRequest request)
    {
        if (request.Op is not ("list" or "fill" or "open" or "save")) throw new VaultException("Unsupported browser request.");
        if (request.Origin is null || request.EntryId is null || request.Username is null || request.Password is null)
            throw new VaultException("Invalid browser request fields.");
        if (request.Op == "open") return "";
        if (request.Origin.Length is < 9 or > 2048) throw new VaultException("Invalid browser origin.");
        if (!Uri.TryCreate(request.Origin, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            throw new VaultException("Browser origin must be an exact HTTPS origin.");
        string host = OriginPolicy.ExactHost(request.Origin);
        if (request.Op == "fill" && !Guid.TryParseExact(request.EntryId, "D", out _))
            throw new VaultException("Invalid saved login identifier.");
        if (request.Op == "save")
        {
            if (request.Username.Length > 2000 || request.Username.IndexOfAny(['\r','\n','\0']) >= 0)
                throw new VaultException("Invalid captured username.");
            // A generated password is provided only after explicit user action in the extension.
            if (request.Password.Length is < 16 or > 128 ||
                request.Password.Any(c => c is < '!' or > '~'))
                throw new VaultException("Invalid generated password.");
        }
        return host;
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct) where T : class
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size is < 2 or > MaxBytes) throw new InvalidDataException("Invalid browser message size.");
        byte[] buffer = new byte[size];
        await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(buffer, new JsonDocumentOptions { MaxDepth = 8 });
            JsonOptions.NoDuplicates(doc.RootElement);
            return JsonSerializer.Deserialize<T>(buffer, JsonOptions.Strict) ?? throw new InvalidDataException("Missing request.");
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions.Strict);
        if (json.Length is < 2 or > MaxBytes) throw new InvalidDataException("Browser message exceeds size limit.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, json.Length);
        try
        {
            await stream.WriteAsync(header, ct).ConfigureAwait(false);
            await stream.WriteAsync(json, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(json); }
    }
}
