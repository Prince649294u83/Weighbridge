using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Settings.Configuration;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.Settings;

public sealed class SettingsPersistenceTests
{
    private static JsonConfigurationWriter WriterFor(TempDataRoot data) =>
        new(data.Paths, NullLogger<JsonConfigurationWriter>.Instance);

    private static void SeedConfiguration(TempDataRoot data, string json)
    {
        data.Paths.EnsureCreated();
        File.WriteAllText(data.Paths.ConfigurationFile, json);
    }

    [Fact]
    public void SecretProtector_AllowsExplicitSecrets_AndDoesNotDoubleEncrypt()
    {
        if (!OperatingSystem.IsWindows()) return;

        string plainSecret = "SuperSecret123";
        string protectedOnce = SecretProtector.Protect(plainSecret);

        Assert.StartsWith(SecretProtector.Prefix, protectedOnce);

        // Idempotency: Protect on already protected string returns same string
        string protectedTwice = SecretProtector.Protect(protectedOnce);
        Assert.Equal(protectedOnce, protectedTwice);

        string? decrypted = SecretProtector.TryUnprotect(protectedOnce);
        Assert.Equal(plainSecret, decrypted);
    }

    [Fact]
    public async Task SaveAsync_ProtectsGsmPinAndHttpApiKey_LeavesConnectionStringPlain()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var data = new TempDataRoot();
        SeedConfiguration(data, """
            {
              "ConnectionStrings": { "DefaultConnection": "Data Source=weighbridge.db" },
              "Sms": { "GsmPin": "1234", "HttpApiKey": "secret-token-xyz" }
            }
            """);

        await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Data Source=weighbridge.db;Foreign Keys=True",
            ["Sms:GsmPin"] = "4321",
            ["Sms:HttpApiKey"] = "updated-key-999"
        });

        var root = JsonNode.Parse(File.ReadAllText(data.Paths.ConfigurationFile))!.AsObject();

        string? connStr = root["ConnectionStrings"]?["DefaultConnection"]?.GetValue<string>();
        string? gsmPin = root["Sms"]?["GsmPin"]?.GetValue<string>();
        string? httpApiKey = root["Sms"]?["HttpApiKey"]?.GetValue<string>();

        // Assert: Connection string is NOT encrypted
        Assert.Equal("Data Source=weighbridge.db;Foreign Keys=True", connStr);
        Assert.False(SecretProtector.IsProtected(connStr));

        // Assert: GsmPin and HttpApiKey ARE protected with DPAPI
        Assert.True(SecretProtector.IsProtected(gsmPin));
        Assert.True(SecretProtector.IsProtected(httpApiKey));

        Assert.Equal("4321", SecretProtector.TryUnprotect(gsmPin));
        Assert.Equal("updated-key-999", SecretProtector.TryUnprotect(httpApiKey));
    }
}
