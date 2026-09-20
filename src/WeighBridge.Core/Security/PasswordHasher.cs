using System.Security.Cryptography;
using System.Text;

namespace WeighBridge.Core.Security;

/// <summary>
/// Shared PBKDF2 password hashing and verification utility.
/// </summary>
public static class PasswordHasher
{
    public const string Pbkdf2Prefix = "pbkdf2-sha256";
    public const int Pbkdf2Iterations = 600_000;
    public const int SaltBytes = 16;
    public const int DigestBytes = 32;

    /// <summary>
    /// Hashes a plaintext password using PBKDF2-SHA256 with 600,000 iterations and a secure random salt.
    /// </summary>
    public static string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            DigestBytes);

        return $"{Pbkdf2Prefix}${Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>
    /// Verifies a plaintext password against a stored PBKDF2 or legacy SHA-256 hash.
    /// </summary>
    public static bool VerifyPassword(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        if (storedHash.StartsWith(Pbkdf2Prefix, StringComparison.OrdinalIgnoreCase))
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4) return false;

            if (!int.TryParse(parts[1], out int iterations)) return false;

            try
            {
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expectedHash = Convert.FromBase64String(parts[3]);

                byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    DigestBytes);

                return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // Legacy Unsalted SHA-256 fallback
        if (IsLegacyDigest(storedHash))
        {
            try
            {
                byte[] expected = Convert.FromBase64String(storedHash);
                byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(password));
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return false;
    }

    public static bool IsLegacyDigest(string hash)
    {
        return !hash.StartsWith(Pbkdf2Prefix, StringComparison.OrdinalIgnoreCase) &&
               hash.Length == 44; // Base64 length of 32-byte SHA256
    }
}
