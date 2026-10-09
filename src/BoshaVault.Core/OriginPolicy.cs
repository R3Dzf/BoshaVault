using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;
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
        try
        {
            string stored = ExactHost(storedUrl), requested = ExactHost(requestedUrl);
            if (stored == requested) return true;
            // Only the literal www. label may be normalized. This is not
            // suffix matching: accounts.example.com is NOT example.com.
            string Plain(string host) => host.StartsWith("www.", StringComparison.Ordinal)
                ? host[4..] : host;
            return Plain(stored) == Plain(requested);
        }
        catch (VaultException) { return false; }
    }
    public enum DomainRelation { None, Exact, Related }

    // The pinned offline PSL includes PRIVATE rules, e.g. github.io; two
    // independent tenants must NEVER be grouped by naïve last-two-label logic.
    private static readonly Lazy<DomainParser?> PublicSuffixParser = new(() =>
    {
        try
        {
            string listPath=Path.Combine(AppContext.BaseDirectory,"public_suffix_list.dat");
            if(!File.Exists(listPath))return null; // fail closed: exact matches still work
            var provider=new LocalFileRuleProvider(listPath);
            provider.BuildAsync().GetAwaiter().GetResult();
            return new DomainParser(provider);
        }
        catch { return null; } // never turn PSL unavailability into permissive matching
    },System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    public static string? RegistrableDomain(string host)
    {
        if(host.Length is < 4 or > 253 || host.EndsWith('.'))return null;
        try
        {
            var domain=PublicSuffixParser.Value?.Parse(host);
            string? registrable=domain?.RegistrableDomain;
            if(string.IsNullOrWhiteSpace(registrable))return null;
            registrable=registrable.ToLowerInvariant();
            // Sanity: parser must never permit a partial-string match.
            if(host!=registrable && !host.EndsWith("."+registrable,StringComparison.Ordinal))
                return null;
            return registrable;
        }
        catch { return null; }
    }
    public static DomainRelation Relation(string storedUrl,string requestedUrl)
    {
        try
        {
            string saved=ExactHost(storedUrl),requested=ExactHost(requestedUrl);
            if(Matches(storedUrl,requestedUrl))return DomainRelation.Exact;
            string? a=RegistrableDomain(saved),b=RegistrableDomain(requested);
            if(a!=null && b!=null && string.Equals(a,b,StringComparison.Ordinal))
                return DomainRelation.Related;
        }
        catch(VaultException) { }
        return DomainRelation.None;
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
