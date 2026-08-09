using Microsoft.Extensions.DependencyInjection;
using WeighBridge.Core.Abstractions;
using WeighBridge.Hardware.Cameras;
using WeighBridge.Hardware.WeightIndicators;

namespace WeighBridge.Hardware.DependencyInjection;

/// <summary>Registers the hardware layer.</summary>
public static class HardwareServiceCollectionExtensions
{
    /// <summary>
    /// Adds the placeholder weight indicator and camera services.
    /// </summary>
    /// <remarks>
    /// The Weight Indicator module replaces these two registrations and nothing else:
    /// every consumer depends on <see cref="IWeightIndicatorService"/>, never on a
    /// concrete type.
    /// </remarks>
    public static IServiceCollection AddWeighBridgeHardware(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IWeightIndicatorService, PlaceholderWeightIndicatorService>();
        services.AddSingleton<ICameraService, PlaceholderCameraService>();

        return services;
    }
}
