namespace WeighBridge.Core.Security;

/// <summary>
/// Payload embedded inside an authorized RSA-signed license.
/// </summary>
public sealed record LicensePayload(
    string HardwareId,
    string LicensedTo,
    DateTime? ExpirationUtc,
    decimal MaxCapacityKg,
    DateTime CreatedUtc,
    string? LicenseType = "Standard");

/// <summary>
/// Result of validating a license token against the local machine and public key.
/// </summary>
public sealed record LicenseValidationResult(
    bool IsValid,
    string Message,
    LicensePayload? Payload,
    bool IsInGracePeriod,
    TimeSpan? GraceRemaining);

/// <summary>
/// Anti-piracy RSA-2048 licensing service.
/// </summary>
public interface ILicenseService
{
    /// <summary>
    /// Gets the current machine's hardware identifier.
    /// </summary>
    string GetHardwareId();

    /// <summary>
    /// Validates an RSA-signed license key.
    /// </summary>
    LicenseValidationResult ValidateLicense(string licenseKey);

    /// <summary>
    /// Generates and signs a license key using an RSA private key.
    /// </summary>
    string GenerateLicense(LicensePayload payload, string rsaPrivateKeyPemOrXml);
}
