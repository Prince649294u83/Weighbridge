using Microsoft.Extensions.DependencyInjection;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Settings;
using WeighBridge.Settings.Configuration;
using WeighBridge.Settings.Services;

namespace WeighBridge.Settings.DependencyInjection;

/// <summary>Registers configuration provisioning and preference persistence.</summary>
public static class SettingsServiceCollectionExtensions
{
    /// <summary>Adds the settings layer.</summary>
    public static IServiceCollection AddWeighBridgeSettings(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ConfigurationProvisioner>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IConfigurationWriter, JsonConfigurationWriter>();

        return services;
    }
}
