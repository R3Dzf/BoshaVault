using System.Globalization;

namespace BoshaVault.Core;

public static class OriginPolicy
{
    // Exact HTTPS hostname; no suffix, title, lookalike, or substring matching.
    public static string ExactHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns)
            throw new VaultException("Use an HTTPS website with no custom port or embedded credentials.");
        string host;
        try { host = new IdnMapping().GetAscii(uri.DnsSafeHost).ToLowerInvariant().TrimEnd('.'); }
        catch (ArgumentException ex) { throw new VaultException("Invalid international hostname.",ex); }
        if (!host.Contains('.') || host.Length > 253) throw new VaultException("Invalid website hostname.");
        return host;
    }
    public static bool Matches(string storedUrl, string requestedUrl)
    {
        try { return ExactHost(storedUrl) == ExactHost(requestedUrl); }
        catch (VaultException) { return false; }
    }
    // Heuristic warning only; never authorizes Autofill or replaces exact-match
    // policy. No saved hostname is disclosed to an untrusted browser site.
    public static bool LooksLikeSavedHostname(string requestedHost,IEnumerable<string> savedHosts)
    {
        if (requestedHost.Length is < 4 or > 253) return false;
        foreach (string stored in savedHosts)
        {
            if (string.Equals(requestedHost,stored,StringComparison.OrdinalIgnoreCase))
                continue;
            if (requestedHost.StartsWith(stored+".",StringComparison.OrdinalIgnoreCase))
                return true; // e.g. legit.example.com.attacker.test
            if (OneEditApart(requestedHost,stored)) return true;
        }
        return requestedHost.Split('.').Any(part=>part.StartsWith("xn--",StringComparison.OrdinalIgnoreCase));
    }
    private static bool OneEditApart(string x,string y)
    {
        if(Math.Abs(x.Length-y.Length)>1 || x.Length<5 || y.Length<5)return false;
        int i=0,j=0,edits=0;
        while(i<x.Length && j<y.Length)
        {
            if(char.ToLowerInvariant(x[i])==char.ToLowerInvariant(y[j])) {i++;j++;continue;}
            if(++edits>1)return false;
            if(x.Length>=y.Length)i++;
            if(y.Length>=x.Length)j++;
        }
        return edits+(x.Length-i)+(y.Length-j)==1;
    }
}
