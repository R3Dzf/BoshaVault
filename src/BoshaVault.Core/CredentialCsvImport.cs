using System.Text;

namespace BoshaVault.Core;

/// <summary>
/// Explicit local-only importer for Chrome/Google Password Manager and Bitwarden
/// login CSV exports. Never writes/export plaintext, never logs credential data.
/// </summary>
public static class CredentialCsvImport
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public const int MaxRows = 2000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public sealed class Plan : IDisposable
    {
        public List<VaultEntry> Entries { get; } = [];
        public int SkippedUnsafe { get; internal set; }
        public int SkippedDuplicate { get; internal set; }
        public int SkippedNonLogin { get; internal set; }
        public int SourceRows { get; internal set; }
        public void Dispose()
        {
            foreach (var e in Entries)
            {
                e.Password = ""; e.Username = ""; e.Notes = "";
                e.Url = ""; e.Title = "";
            }
            Entries.Clear();
        }
    }

    public static Plan Prepare(ReadOnlySpan<byte> bytes, string source,
        IEnumerable<VaultEntry> alreadySaved)
    {
        if (bytes.Length is < 5 or > MaxBytes)
            throw new VaultException("CSV file is empty or exceeds the 2 MB safety limit.");
        if (source is not ("chrome" or "bitwarden"))
            throw new VaultException("Choose Chrome or Bitwarden CSV.");
        string csv;
        try { csv = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException ex)
        { throw new VaultException("CSV must be valid UTF-8.", ex); }
        if (csv.StartsWith('\uFEFF')) csv = csv[1..];
        var rows = ParseRows(csv);
        if (rows.Count < 2) throw new VaultException("CSV needs a header and at least one login.");

        string[] names = rows[0].Select(c => c.Trim().ToLowerInvariant()).ToArray();
        if (names.Length != names.Distinct(StringComparer.Ordinal).Count())
            throw new VaultException("Duplicate CSV header columns.");
        Dictionary<string,int> columns = names.Select((x,i) => (x,i))
            .ToDictionary(x => x.x, x => x.i, StringComparer.Ordinal);
        string[] required = source == "chrome"
            ? ["name","url","username","password"]
            : ["type","name","login_uri","login_username","login_password","login_totp"];
        if (!required.All(columns.ContainsKey))
            throw new VaultException("CSV columns do not match the selected export format.");
        string Value(string[] record,string name) =>
            columns.TryGetValue(name,out int column) && column < record.Length ? record[column] : "";
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in alreadySaved.Where(x => !x.Deleted && x.Url.Length > 0))
        {
            try { known.Add(OriginPolicy.ExactHost(item.Url) + "\0" + item.Username.ToUpperInvariant()); }
            catch (VaultException) { }
        }

        Plan plan = new();
        try
        {
            foreach (string[] cells in rows.Skip(1))
            {
                plan.SourceRows++;
                if (cells.Length != names.Length)
                {
                    plan.SkippedUnsafe++;continue;
                }
                if (source == "bitwarden")
                {
                    if (!string.Equals(Value(cells,"type"),"login",StringComparison.OrdinalIgnoreCase))
                    { plan.SkippedNonLogin++;continue; }
                    // Do not silently lose 2FA secrets or custom fields on import.
                    if (!string.IsNullOrWhiteSpace(Value(cells,"login_totp")) ||
                        !string.IsNullOrWhiteSpace(Value(cells,"fields")))
                        throw new VaultException("Bitwarden export contains TOTP or custom fields. Import is stopped to avoid losing sensitive fields; export a login-only CSV without those fields.");
                }
                string title = Value(cells,"name").Trim();
                string username = (source == "chrome" ? Value(cells,"username") :
                    Value(cells,"login_username")).Trim();
                string password = source == "chrome" ? Value(cells,"password") :
                    Value(cells,"login_password");
                string url = (source == "chrome" ? Value(cells,"url") :
                    Value(cells,"login_uri")).Trim();
                string notes = Value(cells,source == "chrome" ? "note" : "notes");
                string folder = source == "bitwarden" ? Value(cells,"folder").Trim() : "Imported / Chrome";
                string host;
                try { host = OriginPolicy.ExactHost(url); }
                catch (VaultException) { plan.SkippedUnsafe++;continue; }
                if (title.Length > 200 || username.Length is 0 or > 2000 ||
                    password.Length is 0 or > 4096 || notes.Length > 32000 ||
                    folder.Length > 100 || HasControls(username) || HasControls(title) ||
                    HasControls(folder) || HasControls(password))
                { plan.SkippedUnsafe++;continue; }
                string duplicateKey = host+"\0"+username.ToUpperInvariant();
                if (!known.Add(duplicateKey))
                { plan.SkippedDuplicate++;continue; }
                plan.Entries.Add(new VaultEntry {
                    Title = title.Length==0 ? host : title,
                    Url = "https://"+host+"/",
                    Username = username,
                    Password = password,
                    Notes = notes,
                    Folder = folder.Length==0 ? "Imported / Bitwarden" : folder,
                    Favorite = source=="bitwarden" && Value(cells,"favorite") is "1" or "true"
                });
            }
            return plan;
        }
        catch { plan.Dispose(); throw; }
    }

    private static bool HasControls(string s) =>
        s.Any(c => c == '\0' || (c < ' ' && c is not '\r' and not '\n' and not '\t'));
    private static List<string[]> ParseRows(string input)
    {
        List<string[]> rows=[];
        List<string> row=[];
        StringBuilder cell=new();
        bool quoted=false, afterQuote=false;
        void AddCell()
        {
            if (cell.Length > 32768) throw new VaultException("Oversized CSV field.");
            row.Add(cell.ToString()); cell.Clear(); afterQuote=false;
            if (row.Count > 40) throw new VaultException("CSV has too many columns.");
        }
        void AddRow()
        {
            if (row.Count != 1 || row[0].Length!=0)
                rows.Add(row.ToArray());
            row=[];
            if (rows.Count > MaxRows + 1) throw new VaultException("CSV has too many rows.");
        }
        for(int i=0;i<input.Length;i++)
        {
            char c=input[i];
            if(c=='\0') throw new VaultException("CSV contains invalid NUL characters.");
            if(quoted)
            {
                if(c=='"')
                {
                    if(i+1<input.Length && input[i+1]=='"'){cell.Append('"');i++;}
                    else {quoted=false;afterQuote=true;}
                }
                else cell.Append(c);
            }
            else if(afterQuote)
            {
                if(c==',') AddCell();
                else if(c is '\r' or '\n')
                {
                    AddCell();AddRow();
                    if(c=='\r' && i+1<input.Length && input[i+1]=='\n')i++;
                }
                else throw new VaultException("Malformed CSV quoting.");
            }
            else if(c=='"')
            {
                if(cell.Length!=0)throw new VaultException("Malformed CSV quoting.");
                quoted=true;
            }
            else if(c==',')AddCell();
            else if(c is '\r' or '\n')
            {
                AddCell();AddRow();
                if(c=='\r' && i+1<input.Length && input[i+1]=='\n')i++;
            }
            else cell.Append(c);
            if(cell.Length>32768)throw new VaultException("Oversized CSV field.");
        }
        if(quoted)throw new VaultException("Unclosed CSV quoted field.");
        if(cell.Length!=0 || row.Count!=0 || (input.Length>0 && input[^1]==','))
        {AddCell();AddRow();}
        return rows;
    }
}
