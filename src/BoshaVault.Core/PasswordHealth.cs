using System.Text.RegularExpressions;

namespace BoshaVault.Core;

/// <summary>Offline-only password hygiene assessment; no web calls, logging or plaintext exports.</summary>
public static class PasswordHealth
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password","password123","password1234","12345678","123456789",
        "1234567890","qwerty123","qwerty1234","admin123","letmein",
        "welcome123","iloveyou","adminadmin","passw0rd","password1!"
    };

    public sealed record EntryFinding(string EntryId,string Title,string[] Reasons);
    public sealed record Report(int Total,int AtRisk,int Reused,int Short,
        int CommonPassword,int Predictable,List<EntryFinding> Findings);

    public static bool IsPredictable(string password,string username="")
    {
        if (string.IsNullOrEmpty(password)) return true;
        if (Common.Contains(password)) return true;
        if (password.Length >= 8 && password.All(c=>c==password[0])) return true;
        string norm = password.ToLowerInvariant();
        if (norm.Contains("qwerty",StringComparison.Ordinal) ||
            norm.Contains("123456",StringComparison.Ordinal) ||
            norm.Contains("abcdef",StringComparison.Ordinal) ||
            norm.Contains("password",StringComparison.Ordinal))
            return true;
        if (!string.IsNullOrWhiteSpace(username))
        {
            string user = username.Split('@')[0].Trim();
            if (user.Length >= 5 && norm.Contains(user.ToLowerInvariant(),StringComparison.Ordinal))
                return true;
        }
        return false;
    }
    public static Report Audit(IEnumerable<VaultEntry> source)
    {
        var entries = source.Where(e=>!e.Deleted).ToArray();
        var reused = entries.Where(e=>!string.IsNullOrEmpty(e.Password))
            .GroupBy(e=>e.Password,StringComparer.Ordinal)
            .Where(g=>g.Count()>1)
            .SelectMany(g=>g).Select(e=>e.Id).ToHashSet(StringComparer.Ordinal);
        int shortCount=0, commonCount=0,predictableCount=0;
        List<EntryFinding> findings=[];
        foreach(var entry in entries)
        {
            List<string> reasons=[];
            if (entry.Password.Length<16)
            {
                shortCount++;
                reasons.Add("Short or missing password");
            }
            if(reused.Contains(entry.Id))reasons.Add("Password reused in another saved login");
            if(Common.Contains(entry.Password))
            {
                commonCount++;reasons.Add("Common password pattern");
            }
            else if(IsPredictable(entry.Password,entry.Username))
            {
                predictableCount++;reasons.Add("Predictable password pattern");
            }
            if(reasons.Count!=0) findings.Add(new(entry.Id,entry.Title,reasons.ToArray()));
        }
        return new(entries.Length,findings.Count,reused.Count,shortCount,
            commonCount,predictableCount,findings);
    }
}
