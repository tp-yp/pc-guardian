using System.Security.Cryptography;

namespace Guardian.Core.Security;

/// <summary>PBKDF2-SHA256. Format: pbkdf2-sha256$iterations$saltB64$hashB64</summary>
public static class PasswordHasher
{
    public const int DefaultIterations = 600_000;
    private const int SaltSize = 16, HashSize = 32;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Password must not be empty.", nameof(password));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(encoded)) return false;
        var parts = encoded.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iter) || iter < 1) return false;
        byte[] salt, expected;
        try { salt = Convert.FromBase64String(parts[2]); expected = Convert.FromBase64String(parts[3]); }
        catch (FormatException) { return false; }
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
