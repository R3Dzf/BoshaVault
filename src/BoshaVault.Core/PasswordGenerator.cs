using System.Security.Cryptography;

namespace BoshaVault.Core;

public sealed class PasswordOptions
{
    public const string DefaultSymbols = "!@#$%&*+-=?_";
    public int Length { get; set; } = 24;
    public bool Lowercase { get; set; } = true;
    public bool Uppercase { get; set; } = true;
    public bool Digits { get; set; } = true;
    public bool Symbols { get; set; } = true;
    public bool ExcludeAmbiguous { get; set; } = true;
    public string AllowedSymbols { get; set; } = DefaultSymbols;
    public int MinimumDigits { get; set; } = 1;
    public int MinimumSymbols { get; set; } = 1;
    public PasswordOptions Clone() => (PasswordOptions)MemberwiseClone();
}

public static class PasswordGenerator
{
    private static List<(string Alphabet, int Minimum)> Groups(PasswordOptions options)
    {
        if (options.Length is < 8 or > 128) throw new VaultException("Password length must be 8–128.");
        if (options.AllowedSymbols == null || options.AllowedSymbols.Length > 32 ||
            options.AllowedSymbols.Any(c => c is < '!' or > '~' || char.IsLetterOrDigit(c)))
            throw new VaultException("Allowed symbols must be up to 32 printable punctuation characters, without spaces.");
        List<(string, int)> groups = [];
        if (options.Lowercase) groups.Add((options.ExcludeAmbiguous ? "abcdefghijkmnopqrstuvwxyz" : "abcdefghijklmnopqrstuvwxyz", 1));
        if (options.Uppercase) groups.Add((options.ExcludeAmbiguous ? "ABCDEFGHJKLMNPQRSTUVWXYZ" : "ABCDEFGHIJKLMNOPQRSTUVWXYZ", 1));
        if (options.Digits)
        {
            if (options.MinimumDigits is < 1 or > 128) throw new VaultException("Require at least 1–128 digits when digits are enabled.");
            groups.Add((options.ExcludeAmbiguous ? "23456789" : "0123456789", options.MinimumDigits));
        }
        if (options.Symbols)
        {
            if (options.MinimumSymbols is < 1 or > 128 || options.AllowedSymbols.Length == 0)
                throw new VaultException("Choose allowed symbols and require at least 1–128 when symbols are enabled.");
            groups.Add((new string(options.AllowedSymbols.Distinct().ToArray()), options.MinimumSymbols));
        }
        if (groups.Count == 0) throw new VaultException("Enable at least one character group.");
        if (groups.Sum(g => g.Item2) > options.Length) throw new VaultException("The required character counts exceed the password length.");
        return groups;
    }
    public static void Validate(PasswordOptions options) => Groups(options);
    public static string Generate(int length = 24, bool symbols = true) => Generate(new PasswordOptions { Length = length, Symbols = symbols });
    public static string Generate(PasswordOptions options)
    {
        var groups = Groups(options);
        var chars = new char[options.Length];
        var alphabet = string.Concat(groups.Select(g => g.Alphabet));
        int next = 0;
        foreach (var group in groups)
            for (int i = 0; i < group.Minimum; i++) chars[next++] = group.Alphabet[RandomNumberGenerator.GetInt32(group.Alphabet.Length)];
        while (next < chars.Length) chars[next++] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        for (int i = chars.Length - 1; i > 0; i--)
        { int j = RandomNumberGenerator.GetInt32(i + 1); (chars[i], chars[j]) = (chars[j], chars[i]); }
        return new string(chars);
    }
    public static bool Weak(string password) => password.Length < 16;
}
