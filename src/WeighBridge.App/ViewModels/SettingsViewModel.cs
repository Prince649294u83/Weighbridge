using WeighBridge.Domain.Enums;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// Configuration placeholder. Will surface the indicator, printer, camera and server
/// settings that currently live in <c>appsettings.json</c>.
/// </summary>
public sealed class SettingsViewModel : ModulePlaceholderViewModel
{
    public SettingsViewModel()
        : base(
            ApplicationModule.Settings,
            "Settings",
            "Weight indicator, printer, camera and server configuration, plus appearance preferences.",
            "Icon.Settings")
    {
    }
}
