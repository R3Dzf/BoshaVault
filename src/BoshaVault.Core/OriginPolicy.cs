using Nager.PublicSuffix;
using Nager.PublicSuffix.RuleProviders;
using System.Globalization;

namespace BoshaVault.Core;

public sealed class DomainAliasGroup
{
    public string Name { get; set; } = "";
    public List<string> Hosts { get; set; } = [];
}

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
    public enum DomainRelation { None, Exact, TrustedGroup, Related }

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
    // A user-created group is an explicit, local preference; it never permits
    // silent credential disclosure. Only hosts sharing one registrable domain
    // can be grouped. In particular, github.io tenants must remain separate.
    public static void ValidateGroups(IReadOnlyList<DomainAliasGroup>? groups)
    {
        if(groups == null || groups.Count > 24)throw new VaultException("Too many domain groups.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach(var group in groups)
        {
            if(group == null || string.IsNullOrWhiteSpace(group.Name) ||
               group.Name.Length > 60 || group.Name.Any(char.IsControl) ||
               group.Hosts == null || group.Hosts.Count is < 2 or > 16)
                throw new VaultException("Each group needs a name and 2–16 valid HTTPS hosts.");
            string? baseDomain = null;
            foreach(var raw in group.Hosts)
            {
                if(raw == null || raw.Length is < 4 or > 253 || raw.Contains('/') ||
                   raw.Contains(':') || raw.Contains('*') || raw.Contains('?') ||
                   !string.Equals(raw,raw.Trim(),StringComparison.Ordinal))
                    throw new VaultException("Enter hostnames only, like accounts.google.com.");
                var host = ExactHost("https://" + raw);
                if(!string.Equals(host,raw,StringComparison.Ordinal))
                    throw new VaultException("Use lowercase canonical domain names (IDN in punycode).");
                var domain = RegistrableDomain(host);
                if(domain == null || (baseDomain != null && baseDomain != domain))
                    throw new VaultException("Domain groups must belong to one verified registrable domain.");
                baseDomain = domain;
                if(!seen.Add(host))
                    throw new VaultException("One hostname cannot belong to multiple groups.");
            }
        }
    }
    private static bool IsInSameGroup(string saved,string requested,IReadOnlyList<DomainAliasGroup>? groups)
    {
        if(groups == null || groups.Count == 0)return false;
        // Validate on every authorization path, not only when saving UI state.
        try { ValidateGroups(groups); }
        catch(VaultException) { return false; }
        return groups.Any(g=>g.Hosts.Contains(saved,StringComparer.Ordinal) &&
                             g.Hosts.Contains(requested,StringComparer.Ordinal));
    }

    public static DomainRelation Relation(string storedUrl,string requestedUrl,
        IReadOnlyList<DomainAliasGroup>? groups = null)
    {
        try
        {
            string saved=ExactHost(storedUrl),requested=ExactHost(requestedUrl);
            if(Matches(storedUrl,requestedUrl))return DomainRelation.Exact;
            if(IsInSameGroup(saved,requested,groups))return DomainRelation.TrustedGroup;
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
