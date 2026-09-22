using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Security;

namespace WeighBridge.Services.Security;

/// <summary>
/// RSA-2048 cryptographically signed licensing service.
/// Validates offline hardware-locked licenses with signature verification and 7-day grace period.
/// </summary>
public sealed class RsaLicenseService : ILicenseService
{
    // Authoritative built-in RSA-2048 public key (SubjectPublicKeyInfo)
    public const string DefaultPublicKeyBase64 =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAq8dmdEZ5ONp0CiazD9Yjt25Dm9WLA/F8" +
        "B5bS712A3qOwufczFApnqWbLRD4/efGFDRlrsh/aZ9sGmmQ2s07ZRNY7y/Sv2QgGlTaNkfMx8yvN" +
        "rnQFaTxcddBQzkUl26f4wS6jKcCMV+VZSc559iQSo//VDR+Dxbb1lOH/feAM58zCk1KhKX4tF8Y3" +
        "T9uuBkCTy+YtB/YyYNZtCk9fuLf4W/+bISP0bTAzzoNfb0mMnh4gJagTbvdjS8TzT38xWKqVP7fH" +
        "ZQ0eyHkv2kY66/1VsB+X+kAKMEX7WzoyCw3IhKulmUr+RGkbkuktE4w4OqyyiekEpCeLIv1xTmOE" +
        "ur985QIDAQAB";

    private readonly IHardwareIdProvider _hardwareIdProvider;
    private readonly ILogger<RsaLicenseService> _logger;
    private readonly byte[] _publicKeyBytes;

    public RsaLicenseService(
        IHardwareIdProvider hardwareIdProvider,
        ILogger<RsaLicenseService> logger,
        string? customPublicKeyBase64 = null)
    {
        _hardwareIdProvider = hardwareIdProvider ?? throw new ArgumentNullException(nameof(hardwareIdProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _publicKeyBytes = Convert.FromBase64String(customPublicKeyBase64 ?? DefaultPublicKeyBase64);
    }

    public string GetHardwareId() => _hardwareIdProvider.GetHardwareId();

    public LicenseValidationResult ValidateLicense(string licenseKey)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return CheckGracePeriod("No license key configured.");
        }

        var parts = licenseKey.Trim().Split('.');
        if (parts.Length != 2)
        {
            return CheckGracePeriod("Invalid license token format.");
        }

        try
        {
            var payloadJsonBytes = Convert.FromBase64String(parts[0]);
            var signatureBytes = Convert.FromBase64String(parts[1]);

            // Verify RSA-2048 SHA-256 signature
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(_publicKeyBytes, out _);

            bool isSignatureValid = rsa.VerifyData(
                payloadJsonBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            if (!isSignatureValid)
            {
                _logger.LogWarning("License signature verification failed.");
                return new LicenseValidationResult(false, "License signature is invalid or has been tampered with.", null, false, null);
            }

            var payloadJson = Encoding.UTF8.GetString(payloadJsonBytes);
            var payload = JsonSerializer.Deserialize<LicensePayload>(payloadJson);
            if (payload is null)
            {
                return new LicenseValidationResult(false, "License payload is invalid.", null, false, null);
            }

            // Check hardware ID binding (allow "*" for wildcard/demo licenses)
            var currentHardwareId = GetHardwareId();
            if (payload.HardwareId != "*" && !string.Equals(payload.HardwareId, currentHardwareId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Hardware ID mismatch: payload is for {BoundHwid} but current is {CurrentHwid}", payload.HardwareId, currentHardwareId);
                return new LicenseValidationResult(
                    false,
                    $"Hardware ID mismatch: license is locked to hardware {payload.HardwareId} (Current machine: {currentHardwareId}).",
                    payload,
                    false,
                    null);
            }

            // Check expiration
            if (payload.ExpirationUtc.HasValue && payload.ExpirationUtc.Value < DateTime.UtcNow)
            {
                return new LicenseValidationResult(
                    false,
                    $"License expired on {payload.ExpirationUtc.Value:yyyy-MM-dd HH:mm} UTC.",
                    payload,
                    false,
                    null);
            }

            return new LicenseValidationResult(
                true,
                $"License verified for {payload.LicensedTo} (Capacity: {payload.MaxCapacityKg:N0} kg).",
                payload,
                false,
                null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse or validate license key");
            return CheckGracePeriod($"Corrupted license key: {ex.Message}");
        }
    }

    public string GenerateLicense(LicensePayload payload, string rsaPrivateKeyPemOrBase64)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(rsaPrivateKeyPemOrBase64);

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadJsonBytes = Encoding.UTF8.GetBytes(payloadJson);

        using var rsa = RSA.Create();
        if (rsaPrivateKeyPemOrBase64.Contains("BEGIN"))
        {
            rsa.ImportFromPem(rsaPrivateKeyPemOrBase64);
        }
        else
        {
            var keyBytes = Convert.FromBase64String(rsaPrivateKeyPemOrBase64.Trim());
            rsa.ImportPkcs8PrivateKey(keyBytes, out _);
        }

        var signature = rsa.SignData(
            payloadJsonBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var payloadBase64 = Convert.ToBase64String(payloadJsonBytes);
        var signatureBase64 = Convert.ToBase64String(signature);

        return $"{payloadBase64}.{signatureBase64}";
    }

    private LicenseValidationResult CheckGracePeriod(string reason)
    {
        // 7-day grace period
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var markerPath = Path.Combine(localAppData, "WeighBridge", ".license_first_run");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            DateTime firstRunUtc;
            if (File.Exists(markerPath))
            {
                var text = File.ReadAllText(markerPath).Trim();
                if (!DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out firstRunUtc))
                {
                    firstRunUtc = DateTime.UtcNow;
                }
            }
            else
            {
                firstRunUtc = DateTime.UtcNow;
                File.WriteAllText(markerPath, firstRunUtc.ToString("O"));
            }

            var elapsed = DateTime.UtcNow - firstRunUtc;
            var graceDuration = TimeSpan.FromDays(7);
            if (elapsed < graceDuration)
            {
                var remaining = graceDuration - elapsed;
                return new LicenseValidationResult(
                    true,
                    $"{reason} Running in 7-day evaluation grace period ({remaining.Days}d {remaining.Hours}h remaining).",
                    null,
                    true,
                    remaining);
            }
        }
        catch
        {
            // Defensive
        }

        return new LicenseValidationResult(false, $"{reason} Evaluation grace period has expired.", null, false, TimeSpan.Zero);
    }
}
