using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Core.Security;
using WeighBridge.Services.Security;
using Xunit;

namespace WeighBridge.Tests.Security;

public sealed class RsaLicensingTests
{
    private readonly RSA _testRsa;
    private readonly string _publicKeyBase64;
    private readonly string _privateKeyBase64;
    private readonly HardwareIdProvider _hwidProvider;
    private readonly RsaLicenseService _licenseService;

    public RsaLicensingTests()
    {
        _testRsa = RSA.Create(2048);
        _publicKeyBase64 = Convert.ToBase64String(_testRsa.ExportSubjectPublicKeyInfo());
        _privateKeyBase64 = Convert.ToBase64String(_testRsa.ExportPkcs8PrivateKey());

        _hwidProvider = new HardwareIdProvider();
        _licenseService = new RsaLicenseService(
            _hwidProvider,
            NullLogger<RsaLicenseService>.Instance,
            _publicKeyBase64);
    }

    [Fact]
    public void HardwareIdProvider_ReturnsStandardFormat_AndIsDeterministic()
    {
        var id1 = _hwidProvider.GetHardwareId();
        var id2 = _hwidProvider.GetHardwareId();

        Assert.NotNull(id1);
        Assert.Equal(id1, id2);
        Assert.StartsWith("WB-", id1);
        Assert.Equal(22, id1.Length);
        var parts = id1.Split('-');
        Assert.Equal(5, parts.Length);
        Assert.Equal("WB", parts[0]);
        for (int i = 1; i <= 4; i++)
        {
            Assert.Equal(4, parts[i].Length);
        }
    }

    [Fact]
    public void ValidateLicense_WithValidSignedKey_Succeeds()
    {
        var hwid = _hwidProvider.GetHardwareId();
        var payload = new LicensePayload(
            HardwareId: hwid,
            LicensedTo: "Acme Industrial Logistics",
            ExpirationUtc: DateTime.UtcNow.AddYears(1),
            MaxCapacityKg: 100000m,
            CreatedUtc: DateTime.UtcNow);

        var key = _licenseService.GenerateLicense(payload, _privateKeyBase64);
        var result = _licenseService.ValidateLicense(key);

        Assert.True(result.IsValid);
        Assert.False(result.IsInGracePeriod);
        Assert.NotNull(result.Payload);
        Assert.Equal("Acme Industrial Logistics", result.Payload.LicensedTo);
        Assert.Equal(100000m, result.Payload.MaxCapacityKg);
    }

    [Fact]
    public void ValidateLicense_WithWildcardHwid_SucceedsOnAnyMachine()
    {
        var payload = new LicensePayload(
            HardwareId: "*",
            LicensedTo: "Universal Site License",
            ExpirationUtc: DateTime.UtcNow.AddMonths(6),
            MaxCapacityKg: 80000m,
            CreatedUtc: DateTime.UtcNow);

        var key = _licenseService.GenerateLicense(payload, _privateKeyBase64);
        var result = _licenseService.ValidateLicense(key);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Payload);
        Assert.Equal("Universal Site License", result.Payload.LicensedTo);
    }

    [Fact]
    public void ValidateLicense_WithMismatchedHwid_Fails()
    {
        var payload = new LicensePayload(
            HardwareId: "WB-9999-8888-7777-6666",
            LicensedTo: "Wrong Machine",
            ExpirationUtc: DateTime.UtcNow.AddYears(1),
            MaxCapacityKg: 60000m,
            CreatedUtc: DateTime.UtcNow);

        var key = _licenseService.GenerateLicense(payload, _privateKeyBase64);
        var result = _licenseService.ValidateLicense(key);

        Assert.False(result.IsValid);
        Assert.Contains("mismatch", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLicense_WithExpiredDate_Fails()
    {
        var hwid = _hwidProvider.GetHardwareId();
        var payload = new LicensePayload(
            HardwareId: hwid,
            LicensedTo: "Expired Client",
            ExpirationUtc: DateTime.UtcNow.AddDays(-1),
            MaxCapacityKg: 50000m,
            CreatedUtc: DateTime.UtcNow.AddYears(-1));

        var key = _licenseService.GenerateLicense(payload, _privateKeyBase64);
        var result = _licenseService.ValidateLicense(key);

        Assert.False(result.IsValid);
        Assert.Contains("expired", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLicense_WithTamperedSignature_Fails()
    {
        var hwid = _hwidProvider.GetHardwareId();
        var payload = new LicensePayload(
            HardwareId: hwid,
            LicensedTo: "Hacked Client",
            ExpirationUtc: DateTime.UtcNow.AddYears(1),
            MaxCapacityKg: 50000m,
            CreatedUtc: DateTime.UtcNow);

        var key = _licenseService.GenerateLicense(payload, _privateKeyBase64);
        var parts = key.Split('.');

        var tamperedPayload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("{\"HardwareId\":\"*\",\"LicensedTo\":\"Pirate\"}"));
        var tamperedKey = $"{tamperedPayload}.{parts[1]}";

        var result = _licenseService.ValidateLicense(tamperedKey);
        Assert.False(result.IsValid);
        Assert.Contains("tampered", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateLicense_WhenUnlicensed_AllowsGracePeriod()
    {
        var result = _licenseService.ValidateLicense(string.Empty);
        Assert.True(result.IsValid);
        Assert.True(result.IsInGracePeriod);
        Assert.NotNull(result.GraceRemaining);
    }
}
