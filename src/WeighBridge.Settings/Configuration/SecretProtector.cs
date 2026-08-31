using System.Security.Cryptography;
using System.Text;

namespace WeighBridge.Settings.Configuration;

/// <summary>
/// Protects machine-local secrets (camera passwords, server API keys, GSM PINs) with Windows DPAPI
/// at the current user's scope.
/// </summary>
/// <remarks>
/// <para>
/// A secret in <c>appsettings.json</c> is readable by anyone who can read the file — and
/// the file sits in <c>%LOCALAPPDATA%</c> precisely so it can be backed up and inspected.
/// Values stored through <see cref="Protect"/> carry a <c>dpapi:</c> prefix and decrypt
/// only for the same Windows user on the same machine, which is exactly the audience a
/// terminal's credentials were meant to have.
/// </para>
/// <para>
/// Normal configuration keys and SQLite connection strings remain unencrypted structured JSON.
/// </para>
/// </remarks>
public static class SecretProtector
{
    /// <summary>Marks a value as DPAPI-protected.</summary>
    public const string Prefix = "dpapi:";

    /// <summary>The authoritative explicit allowlist of leaf keys whose values are secrets.</summary>
    public static readonly IReadOnlyList<string> ProtectedLeafKeys =
    [
        "Password",
        "ApiKey",
        "ApiSecret",
        "GsmPin",
        "HttpApiKey"
    ];

    /// <summary>Checks whether a string has a valid DPAPI prefix.</summary>
    public static bool IsProtected(string? value)
        => !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Encrypts for the current Windows user. Idempotent and validates against double-encryption.</summary>
    /// <exception cref="PlatformNotSupportedException">On non-Windows platforms.</exception>
    public static string Protect(string plainText)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainText);

        if (IsProtected(plainText))
        {
            // Already protected; do not double-encrypt
            return plainText;
        }

        // This terminal ships on Windows; DPAPI simply has no counterpart elsewhere.
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Secret protection requires Windows (DPAPI).");
        }

        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plainText),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        return Prefix + Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Decrypts a protected value, returning <c>null</c> when it cannot be decrypted —
    /// typically because it came from another user profile or another machine, or because
    /// the platform cannot decrypt at all. Callers treat that like an unset secret rather
    /// than crashing the terminal.
    /// </summary>
    public static string? TryUnprotect(string? value)
    {
        if (!IsProtected(value))
        {
            return value;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var bytes = ProtectedData.Unprotect(
                Convert.FromBase64String(value![Prefix.Length..]),
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
