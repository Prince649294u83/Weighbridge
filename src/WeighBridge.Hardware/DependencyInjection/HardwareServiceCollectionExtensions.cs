using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Hardware.Cameras;
using WeighBridge.Hardware.WeightIndicators;

namespace WeighBridge.Hardware.DependencyInjection;

/// <summary>Registers the hardware layer.</summary>
public static class HardwareServiceCollectionExtensions
{
    /// <summary>
    /// Adds the weight indicator, camera services, and protocol parsers to DI.
    /// </summary>
    public static IServiceCollection AddWeighBridgeHardware(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Core hardware components
        services.AddSingleton<IFrameExtractor, DelimitedFrameExtractor>();
        services.AddSingleton<IIndicatorProtocolParser, GenericAsciiProtocolParser>();
        services.AddSingleton<IWeightDecoder, WeightDecoder>();
        services.AddTransient<ISerialPortTransport>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<HardwareOptions>>().Value.WeightIndicator;
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SerialPortTransport>>();
            return new SerialPortTransport(options, logger);
        });

        // Finds which port the indicator is on. Registered whatever the DriverType is: the
        // reason to scan is usually that the configured port is wrong, so a configuration
        // that currently resolves to the placeholder is exactly when it is needed.
        services.AddSingleton<IIndicatorPortScanner, SerialPortScanner>();

        // Drivers / Simulators
        services.AddSingleton<WeightIndicatorSimulator>();
        services.AddSingleton<IWeightIndicatorSimulator>(sp => sp.GetRequiredService<WeightIndicatorSimulator>());
        services.AddSingleton<WeightIndicatorService>();
        services.AddSingleton<PlaceholderWeightIndicatorService>();

        // Which implementation serves IWeightIndicatorService is a safety decision, not a
        // convenience. A simulator produces numbers indistinguishable from a weighment at a
        // glance, and this is a weighbridge, so the mapping is explicit and the simulator is
        // reachable only by asking for it by name. An unrecognised DriverType resolves to the
        // placeholder - which reports "not connected" and weighs nothing - because the fallback
        // used to be the simulator, and a typo in one configuration key was therefore enough
        // to put generated weights onto a printed slip.
        services.AddSingleton<IWeightIndicatorService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<HardwareOptions>>().Value.WeightIndicator;

            if (!options.Enabled)
            {
                return sp.GetRequiredService<PlaceholderWeightIndicatorService>();
            }

            if (options.DriverType.Equals("Serial", StringComparison.OrdinalIgnoreCase))
            {
                return sp.GetRequiredService<WeightIndicatorService>();
            }

            if (options.DriverType.Equals("Simulator", StringComparison.OrdinalIgnoreCase))
            {
                return sp.GetRequiredService<WeightIndicatorSimulator>();
            }

            if (!options.DriverType.Equals("Disabled", StringComparison.OrdinalIgnoreCase))
            {
                sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(HardwareServiceCollectionExtensions).FullName!)
                    .LogError(
                        "Hardware:WeightIndicator:DriverType is '{DriverType}', which is not one of " +
                        "Serial, Simulator or Disabled. No weight indicator will be used; weights must " +
                        "be entered manually until the configuration is corrected.",
                        options.DriverType);
            }

            return sp.GetRequiredService<PlaceholderWeightIndicatorService>();
        });

        services.AddSingleton<AuxiliaryYardDisplayService>();

        // Cameras
        services.AddSingleton<CameraService>();
        services.AddSingleton<PlaceholderCameraService>();
        // CameraService generates its images: there is no capture implementation behind it yet,
        // so what it files against a weighment is a synthetic JPEG. That is acceptable for a
        // demonstration and is not acceptable on a slip, so it is reachable only when the
        // configuration turns cameras on. The options were already read here and then ignored,
        // which meant a site with Camera:Enabled false still got generated photographs.
        services.AddSingleton<ICameraService>(sp =>
            sp.GetRequiredService<IOptions<CameraOptions>>().Value.Enabled
                ? sp.GetRequiredService<CameraService>()
                : sp.GetRequiredService<PlaceholderCameraService>());

        return services;
    }
}
