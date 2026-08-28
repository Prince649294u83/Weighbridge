using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Settings.Configuration;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Settings;

/// <summary>
/// Covers the Settings screen's write path: an operator whose indicator moved to a
/// different COM port must be able to fix it in the application, and doing so must not
/// cost them any other configuration.
/// </summary>
public sealed class JsonConfigurationWriterTests
{
    private static JsonConfigurationWriter WriterFor(TempDataRoot data) =>
        new(data.Paths, NullLogger<JsonConfigurationWriter>.Instance);

    private static void SeedConfiguration(TempDataRoot data, string json)
    {
        data.Paths.EnsureCreated();
        File.WriteAllText(data.Paths.ConfigurationFile, json);
    }

    [Fact]
    public async Task SaveAsync_WritesTheValueAtItsConfigurationPath()
    {
        using var data = new TempDataRoot();
        SeedConfiguration(data, """
            { "Hardware": { "WeightIndicator": { "PortName": "COM1", "BaudRate": 9600 } } }
            """);

        await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["Hardware:WeightIndicator:PortName"] = "COM7",
            ["Hardware:WeightIndicator:BaudRate"] = 19200,
        });

        // Read back through the configuration system that will actually consume it, not
        // through the JSON API that wrote it.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(data.Paths.ConfigurationFile)
            .Build();

        Assert.Equal("COM7", configuration["Hardware:WeightIndicator:PortName"]);
        Assert.Equal("19200", configuration["Hardware:WeightIndicator:BaudRate"]);
    }

    [Fact]
    public async Task SaveAsync_LeavesEverySectionItWasNotAskedToChange()
    {
        // The reason this writer edits the document in place instead of serialising an
        // options object over it: an options class only knows the keys it binds, so writing
        // one out would delete the Logging section and the protected secrets with it.
        using var data = new TempDataRoot();
        SeedConfiguration(data, """
            {
              "Application": { "Name": "Site A" },
              "Hardware": { "WeightIndicator": { "PortName": "COM1" } },
              "Camera": { "Password": "enc:something", "RetentionDays": 45 },
              "Logging": { "LogLevel": { "Default": "Warning" } }
            }
            """);

        await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["Hardware:WeightIndicator:PortName"] = "COM3",
        });

        var root = JsonNode.Parse(File.ReadAllText(data.Paths.ConfigurationFile))!.AsObject();

        Assert.Equal("COM3", root["Hardware"]!["WeightIndicator"]!["PortName"]!.GetValue<string>());
        Assert.Equal("Site A", root["Application"]!["Name"]!.GetValue<string>());
        Assert.Equal("enc:something", root["Camera"]!["Password"]!.GetValue<string>());
        Assert.Equal(45, root["Camera"]!["RetentionDays"]!.GetValue<int>());
        Assert.Equal("Warning", root["Logging"]!["LogLevel"]!["Default"]!.GetValue<string>());
    }

    [Fact]
    public async Task SaveAsync_CreatesMissingSections()
    {
        using var data = new TempDataRoot();
        SeedConfiguration(data, """{ "Application": { "Name": "Site A" } }""");

        await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["Hardware:WeightIndicator:PortName"] = "COM4",
        });

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(data.Paths.ConfigurationFile)
            .Build();

        Assert.Equal("COM4", configuration["Hardware:WeightIndicator:PortName"]);
    }

    [Fact]
    public async Task SaveAsync_ReplacesAScalarStandingWhereASectionIsNeeded()
    {
        // A hand edit that left a string at "Hardware" must not stop the operator saving
        // a port number through the screen.
        using var data = new TempDataRoot();
        SeedConfiguration(data, """{ "Hardware": "oops" }""");

        await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["Hardware:WeightIndicator:PortName"] = "COM5",
        });

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(data.Paths.ConfigurationFile)
            .Build();

        Assert.Equal("COM5", configuration["Hardware:WeightIndicator:PortName"]);
    }

    [Fact]
    public async Task SaveAsync_OnACorruptFile_StillSaves()
    {
        // Refusing here would leave the operator unable to correct a bad port number
        // through the only screen that offers to do it.
        using var data = new TempDataRoot();
        SeedConfiguration(data, "{ this is not json");

        await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["Hardware:WeightIndicator:PortName"] = "COM6",
        });

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(data.Paths.ConfigurationFile)
            .Build();

        Assert.Equal("COM6", configuration["Hardware:WeightIndicator:PortName"]);
    }

    [Fact]
    public async Task SaveAsync_OnAMissingFile_CreatesOne()
    {
        using var data = new TempDataRoot();
        data.Paths.EnsureCreated();

        var path = await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["Hardware:WeightIndicator:PortName"] = "COM8",
        });

        Assert.True(File.Exists(path));
        Assert.Equal(data.Paths.ConfigurationFile, path);
    }

    [Fact]
    public async Task SaveAsync_ProtectsASecretBeforeItReachesTheFile()
    {
        using var data = new TempDataRoot();
        SeedConfiguration(data, """{ "Camera": { "Password": "" } }""");

        await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["Camera:Password"] = "typed-by-the-operator",
        });

        var stored = JsonNode.Parse(File.ReadAllText(data.Paths.ConfigurationFile))!
            .AsObject()["Camera"]!["Password"]!.GetValue<string>();

        Assert.NotEqual("typed-by-the-operator", stored);
        Assert.True(SecretProtector.IsProtected(stored));
        Assert.Equal("typed-by-the-operator", SecretProtector.TryUnprotect(stored));
    }

    [Fact]
    public async Task SaveAsync_DoesNotDoubleProtectAnAlreadyProtectedSecret()
    {
        using var data = new TempDataRoot();
        SeedConfiguration(data, """{ "Camera": { "Password": "" } }""");

        var alreadyProtected = SecretProtector.Protect("original");
        await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["Camera:Password"] = alreadyProtected,
        });

        var stored = JsonNode.Parse(File.ReadAllText(data.Paths.ConfigurationFile))!
            .AsObject()["Camera"]!["Password"]!.GetValue<string>();

        Assert.Equal("original", SecretProtector.TryUnprotect(stored));
    }

    [Fact]
    public async Task SaveAsync_WithANullValue_RemovesTheKey()
    {
        using var data = new TempDataRoot();
        SeedConfiguration(data, """{ "Printer": { "DefaultPrinterName": "Old-Printer" } }""");

        await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["Printer:DefaultPrinterName"] = null,
        });

        var root = JsonNode.Parse(File.ReadAllText(data.Paths.ConfigurationFile))!.AsObject();
        Assert.False(root["Printer"]!.AsObject().ContainsKey("DefaultPrinterName"));
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTemporaryFileBehind()
    {
        using var data = new TempDataRoot();
        SeedConfiguration(data, """{ "Application": { "Name": "Site A" } }""");

        await WriterFor(data).SaveAsync(new Dictionary<string, object?>
        {
            ["Hardware:WeightIndicator:PortName"] = "COM9",
        });

        Assert.Empty(Directory.GetFiles(data.Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SaveAsync_ConcurrentWrites_DoNotLoseEachOther()
    {
        // Each save reads, edits and rewrites the whole document, so without the write gate
        // the last one in would silently drop the others' changes.
        using var data = new TempDataRoot();
        SeedConfiguration(data, """{ "Application": { "Name": "Site A" } }""");
        var writer = WriterFor(data);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(i =>
            writer.SaveAsync(new Dictionary<string, object?> { [$"Probe:Key{i}"] = i })));

        var probe = JsonNode.Parse(File.ReadAllText(data.Paths.ConfigurationFile))!
            .AsObject()["Probe"]!.AsObject();

        Assert.Equal(12, probe.Count);
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal(i, probe[$"Key{i}"]!.GetValue<int>());
        }
    }

    [Fact]
    public async Task SaveAsync_WithAnEmptyPath_IsRejected()
    {
        using var data = new TempDataRoot();
        data.Paths.EnsureCreated();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            WriterFor(data).SaveAsync(new Dictionary<string, object?> { [":"] = "value" }));
    }
}
