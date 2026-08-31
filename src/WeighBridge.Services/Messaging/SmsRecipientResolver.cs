using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;

namespace WeighBridge.Services.Messaging;

/// <summary>
/// Resolves and normalizes destination phone numbers following strict recipient precedence.
/// </summary>
public sealed class SmsRecipientResolver(IOptions<SmsOptions> options)
{
    private static readonly Regex PhoneDigitsPattern = new(@"[^\d+]", RegexOptions.Compiled);

    private readonly SmsOptions _options = options?.Value ?? new SmsOptions();

    /// <summary>
    /// Resolves recipient phone number in precedence order: Explicit Override -> Party Phone -> DefaultRecipient -> null.
    /// </summary>
    public string? ResolveRecipient(string? explicitOverride, string? partyPhoneNumber)
    {
        if (TryNormalize(explicitOverride, out var normalizedOverride))
        {
            return normalizedOverride;
        }

        if (TryNormalize(partyPhoneNumber, out var normalizedParty))
        {
            return normalizedParty;
        }

        if (TryNormalize(_options.DefaultRecipient, out var normalizedDefault))
        {
            return normalizedDefault;
        }

        return null;
    }

    /// <summary>
    /// Normalizes and validates basic phone number syntax.
    /// </summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var cleaned = PhoneDigitsPattern.Replace(input.Trim(), "");
        if (cleaned.Length < 7 || cleaned.Length > 16)
        {
            return false;
        }

        normalized = cleaned;
        return true;
    }
}
