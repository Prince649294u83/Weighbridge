using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;
using WeighBridge.Settings.Configuration;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Settings;

/// <summary>
/// Covers the spec requirement that the application must never fail to start because
/// <c>appsettings.json</c> is missing, incomplete or corrupt.
/// </summary>
public sealed class ConfigurationProvisionerTests
{
    [Fact]
    public void EnsureConfigurationFile_OnFirstRun_CreatesAValidFile()
    {
        using var data = new TempDataRoot();
        var provisioner = new ConfigurationProvisioner(data.Paths);

        var path = provisioner.EnsureConfigurationFile();

        Assert.Equal(ConfigurationProvisioningOutcome.Created, provisioner.LastOutcome);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.GetFiles(data.Root, "*.tmp"));

        // The file must be loadable by the configuration system that will consume it.
        var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();
        Assert.False(string.IsNullOrWhiteSpace(configuration["Application:Name"]));
    }

    [Fact]
    public void EnsureConfigurationFile_OnSecondRun_LeavesTheFileUnchanged()
    {
        using var data = new TempDataRoot();
        var provisioner = new ConfigurationProvisioner(data.Paths);
        var path = provisioner.EnsureConfigurationFile();
        var original = File.ReadAllText(path);

        var second = new ConfigurationProvisioner(data.Paths);
        second.EnsureConfigurationFile();

        Assert.Equal(ConfigurationProvisioningOutcome.Unchanged, second.LastOutcome);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void EnsureConfigurationFile_PreservesOperatorValuesWhileAddingMissingKeys()
    {
        // The upgrade path: a newer build introduces keys, but anything the operator
        // configured must survive untouched.
        using var data = new TempDataRoot();
        data.Paths.EnsureCreated();
        File.WriteAllText(
            data.Paths.ConfigurationFile,
            """
            { "Application": { "Name": "Operator Renamed This" } }
            """);

        var provisioner = new ConfigurationProvisioner(data.Paths);
        provisioner.EnsureConfigurationFile();

        Assert.Equal(ConfigurationProvisioningOutcome.Repaired, provisioner.LastOutcome);

        var root = JsonNode.Parse(File.ReadAllText(data.Paths.ConfigurationFile))!.AsObject();
        Assert.Equal("Operator Renamed This", root["Application"]!["Name"]!.GetValue<string>());

        // And the sections that were absent are now present.
        Assert.True(root.ContainsKey("Logging"));
        Assert.True(root.ContainsKey("Database"));
    }

    [Fact]
    public void EnsureConfigurationFile_WithCorruptFile_QuarantinesAndRegenerates()
    {
        using var data = new TempDataRoot();
        data.Paths.EnsureCreated();
        File.WriteAllText(data.Paths.ConfigurationFile, "{ not valid json at all");

        var provisioner = new ConfigurationProvisioner(data.Paths);
        var path = provisioner.EnsureConfigurationFile();

        Assert.Equal(ConfigurationProvisioningOutcome.Recovered, provisioner.LastOutcome);

        // Regenerated and parseable again.
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(document.RootElement.TryGetProperty("Application", out _));

        // The bad file is kept for support rather than silently destroyed.
        Assert.NotEmpty(Directory.GetFiles(data.Root, "appsettings.*"));
    }

    [Fact]
    public void EnsureConfigurationFile_WithMissingDataRoot_CreatesTheWholeLayout()
    {
        using var data = new TempDataRoot();
        Assert.False(Directory.Exists(data.Root));

        var provisioner = new ConfigurationProvisioner(data.Paths);
        provisioner.EnsureConfigurationFile();

        Assert.True(Directory.Exists(data.Root));
        Assert.True(Directory.Exists(data.Paths.LogsDirectory));
        Assert.True(Directory.Exists(data.Paths.DatabaseDirectory));
    }

    [Fact]
    public void EnsureConfigurationFile_WithNonObjectRoot_RecoversToDefaults()
    {
        // A valid JSON document that is nonetheless unusable as configuration.
        using var data = new TempDataRoot();
        data.Paths.EnsureCreated();
        File.WriteAllText(data.Paths.ConfigurationFile, "[1, 2, 3]");

        var provisioner = new ConfigurationProvisioner(data.Paths);
        provisioner.EnsureConfigurationFile();

        Assert.Equal(ConfigurationProvisioningOutcome.Recovered, provisioner.LastOutcome);

        var root = JsonNode.Parse(File.ReadAllText(data.Paths.ConfigurationFile));
        Assert.NotNull(root!.AsObject());
    }
}
