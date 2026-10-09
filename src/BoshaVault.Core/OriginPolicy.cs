using System.Globalization;

namespace BoshaVault.Core;

public static class OriginPolicy
{
    // Exact HTTPS hostname; no suffix, title, lookalike, or substring matching.
    public static string ExactHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns)
            throw new VaultException("Use an HTTPS website with no custom port or embedded credentials.");
        var host = new IdnMapping().GetAscii(uri.DnsSafeHost).ToLowerInvariant().TrimEnd('.');
        if (!host.Contains('.') || host.Length > 253) throw new VaultException("Invalid website hostname.");
        return host;
    }
    public static bool Matches(string storedUrl, string requestedUrl)
    {
        try { return ExactHost(storedUrl) == ExactHost(requestedUrl); }
        catch (VaultException) { return false; }
    }
}
