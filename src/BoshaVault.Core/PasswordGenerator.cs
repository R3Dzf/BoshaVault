using System.Security.Cryptography;

namespace BoshaVault.Core;

public static class PasswordGenerator
{
    public static string Generate(int length = 24, bool symbols = true)
    {
        if (length is < 16 or > 128) throw new VaultException("Password length must be 16–128.");
        string[] groups = symbols
            ? ["abcdefghijkmnopqrstuvwxyz", "ABCDEFGHJKLMNPQRSTUVWXYZ", "23456789", "!@#$%&*+-=?_"]
            : ["abcdefghijkmnopqrstuvwxyz", "ABCDEFGHJKLMNPQRSTUVWXYZ", "23456789"];
        var chars = new char[length];
        var alphabet = string.Concat(groups);
        for (int i = 0; i < groups.Length; i++) chars[i] = groups[i][RandomNumberGenerator.GetInt32(groups[i].Length)];
        for (int i = groups.Length; i < length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        for (int i = length - 1; i > 0; i--)
        { int j = RandomNumberGenerator.GetInt32(i + 1); (chars[i], chars[j]) = (chars[j], chars[i]); }
        return new string(chars);
    }
    public static bool Weak(string password) => password.Length < 16;
}
