using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using WeighBridge.Core.Settings;
using WeighBridge.Core.Theming;
using WeighBridge.Settings.Services;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Settings;

/// <summary>
/// Covers persistence of <see cref="UserPreferences"/>, including the window placement
/// values that proved fragile during foundation bring-up.
/// </summary>
public sealed class SettingsServiceTests
{
    private static SettingsService CreateService(TempDataRoot data) =>
        new(data.Paths, NullLogger<SettingsService>.Instance);

    [Fact]
    public async Task LoadAsync_WithNoFile_YieldsDefaultsAndDoesNotThrow()
    {
        using var data = new TempDataRoot();
        var service = CreateService(data);

        await service.LoadAsync();

        // A missing preferences file is the first-run case, not an error.
        Assert.False(File.Exists(data.Paths.UserPreferencesFile));
        Assert.Equal(AppTheme.Light, service.Preferences.Theme);
        Assert.False(service.Preferences.Window.HasPosition);
    }

    [Fact]
    public async Task SaveAsync_WithDefaultPreferences_WritesFile()
    {
        // Regression test: Left/Top once defaulted to double.NaN, which System.Text.Json
        // refuses to write. Every save threw part-way through, leaving a .tmp behind and
        // silently losing all preferences until a position happened to be captured.
        using var data = new TempDataRoot();
        var service = CreateService(data);

        await service.SaveAsync();

        Assert.True(File.Exists(data.Paths.UserPreferencesFile));
        Assert.Empty(Directory.GetFiles(data.Root, "*.tmp"));

        // Parseable, not merely present.
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(data.Paths.UserPreferencesFile));
        Assert.True(document.RootElement.TryGetProperty("Window", out _));
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsEveryValue()
    {
        using var data = new TempDataRoot();
        var service = CreateService(data);

        service.Preferences.Theme = AppTheme.Dark;
        service.Preferences.IsNavigationCollapsed = true;
        service.Preferences.LastModule = "Reports";
        service.Preferences.Window.Left = 404;
        service.Preferences.Window.Top = 180;
        service.Preferences.Window.Width = 1024;
        service.Preferences.Window.Height = 700;
        service.Preferences.Window.IsMaximized = true;

        await service.SaveAsync();

        // A second instance proves the values came off disk rather than out of memory.
        var reloaded = CreateService(data);
        await reloaded.LoadAsync();

        Assert.Equal(AppTheme.Dark, reloaded.Preferences.Theme);
        Assert.True(reloaded.Preferences.IsNavigationCollapsed);
        Assert.Equal("Reports", reloaded.Preferences.LastModule);
        Assert.Equal(404, reloaded.Preferences.Window.Left);
        Assert.Equal(180, reloaded.Preferences.Window.Top);
        Assert.Equal(1024, reloaded.Preferences.Window.Width);
        Assert.Equal(700, reloaded.Preferences.Window.Height);
        Assert.True(reloaded.Preferences.Window.IsMaximized);
    }

    [Fact]
    public async Task SaveAsync_RaisesPreferencesSaved()
    {
        using var data = new TempDataRoot();
        var service = CreateService(data);
        var raised = 0;
        service.PreferencesSaved += (_, _) => raised++;

        await service.SaveAsync();

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task LoadAsync_WithCorruptFile_FallsBackToDefaults()
    {
        // An operator editing the file by hand, or a power cut mid-write, must not stop
        // the application from starting.
        using var data = new TempDataRoot();
        data.Paths.EnsureCreated();
        await File.WriteAllTextAsync(data.Paths.UserPreferencesFile, "{ this is not json");

        var service = CreateService(data);
        await service.LoadAsync();

        Assert.Equal(AppTheme.Light, service.Preferences.Theme);
    }

    [Fact]
    public async Task SaveAsync_CalledConcurrently_LeavesNoTempFiles()
    {
        // SaveAsync serialises through a semaphore and writes via a .tmp then File.Move.
        // Overlapping callers must not leave residue or corrupt the destination.
        using var data = new TempDataRoot();
        var service = CreateService(data);

        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => service.SaveAsync()));

        Assert.Empty(Directory.GetFiles(data.Root, "*.tmp"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(data.Paths.UserPreferencesFile));
        Assert.True(document.RootElement.TryGetProperty("Theme", out _));
    }

    [Fact]
    public async Task ResetAsync_RestoresDefaultsAndPersistsThem()
    {
        using var data = new TempDataRoot();
        var service = CreateService(data);
        service.Preferences.Theme = AppTheme.Dark;
        service.Preferences.LastModule = "Masters";
        await service.SaveAsync();

        await service.ResetAsync();

        Assert.Equal(AppTheme.Light, service.Preferences.Theme);
        Assert.Null(service.Preferences.LastModule);

        var reloaded = CreateService(data);
        await reloaded.LoadAsync();
        Assert.Equal(AppTheme.Light, reloaded.Preferences.Theme);
    }
}
