using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Application;
using WeighBridge.Core.DependencyInjection;
using WeighBridge.Hardware.Cameras;
using WeighBridge.Hardware.DependencyInjection;
using WeighBridge.Hardware.WeightIndicators;
using WeighBridge.Infrastructure.DependencyInjection;
using WeighBridge.Services.DependencyInjection;
using WeighBridge.Tests.Infrastructure;
using Xunit;

namespace WeighBridge.Tests.DependencyInjection;

public sealed class HardwareRegistrationTests
{
    private static ServiceProvider BuildProvider(params (string Key, string? Value)[] settings)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IApplicationPaths>(new TempDataRoot().Paths);
        services.AddWeighBridgeCore(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build());
        services.AddWeighBridgeInfrastructure();
        services.AddWeighBridgeHardware();
        services.AddWeighBridgeServices();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Theory]
    [InlineData(typeof(IFrameExtractor))]
    [InlineData(typeof(IIndicatorProtocolParser))]
    [InlineData(typeof(IWeightIndicatorService))]
    [InlineData(typeof(IWeightIndicatorSimulator))]
    [InlineData(typeof(ICameraService))]
    public void HardwareServices_Resolve(Type serviceType)
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService(serviceType));
    }

    /// <summary>
    /// Which implementation serves <see cref="IWeightIndicatorService"/> decides whether the
    /// weights on a slip came from the bridge or were generated. The simulator used to be the
    /// fallback for every DriverType the resolver did not recognise, so a typo in one
    /// configuration key was enough to weigh vehicles with invented numbers - and it was also
    /// the shipped default, so a fresh installation did exactly that with nothing mistyped.
    /// </summary>
    [Theory]
    [InlineData("Serial", typeof(WeightIndicatorService))]
    [InlineData("serial", typeof(WeightIndicatorService))]
    [InlineData("Simulator", typeof(WeightIndicatorSimulator))]
    [InlineData("Disabled", typeof(PlaceholderWeightIndicatorService))]
    [InlineData("Smulator", typeof(PlaceholderWeightIndicatorService))]
    [InlineData("", typeof(PlaceholderWeightIndicatorService))]
    public async Task WeightIndicator_IsChosenByDriverType_AndNeverFallsBackToTheSimulator(
        string driverType,
        Type expected)
    {
        // Disposed asynchronously, as the application disposes it: resolving the serial driver
        // creates a SerialPortTransport, which is IAsyncDisposable only, and a container
        // tracking one refuses a synchronous Dispose.
        await using var provider = BuildProvider(
            ("Hardware:WeightIndicator:Enabled", "true"),
            ("Hardware:WeightIndicator:DriverType", driverType));

        Assert.IsType(expected, provider.GetRequiredService<IWeightIndicatorService>());
    }

    /// <summary>
    /// Nothing configured at all has to be safe too: the default DriverType is a code default,
    /// and it was "Simulator".
    /// </summary>
    [Fact]
    public void WeightIndicator_WithNothingConfigured_IsNotTheSimulator()
    {
        using var provider = BuildProvider();

        Assert.IsType<PlaceholderWeightIndicatorService>(
            provider.GetRequiredService<IWeightIndicatorService>());
    }

    /// <summary>
    /// The camera implementation generates the images it files against a weighment, so it must
    /// be reachable only when a site turns cameras on deliberately. The factory read the
    /// options into a local and then ignored it, which is how a terminal with cameras switched
    /// off still got synthetic photographs attached to its slips.
    /// </summary>
    [Theory]
    [InlineData(null, typeof(PlaceholderCameraService))]
    [InlineData("false", typeof(PlaceholderCameraService))]
    [InlineData("true", typeof(CameraService))]
    public void Camera_HonoursTheEnabledSetting(string? enabled, Type expected)
    {
        using var provider = enabled is null
            ? BuildProvider()
            : BuildProvider(("Camera:Enabled", enabled));

        Assert.IsType(expected, provider.GetRequiredService<ICameraService>());
    }

    [Theory]
    [InlineData("false", 0)]
    [InlineData("False", 0)]
    [InlineData("true", 1)]
    [InlineData("True", 1)]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    public void HardwareOptions_DummyZero_HandlesBooleanConfiguration(string dummyZeroValue, int expected)
    {
        using var provider = BuildProvider(("Hardware:WeightIndicator:Decoding:DummyZero", dummyZeroValue));
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<WeighBridge.Core.Configuration.HardwareOptions>>().Value;
        Assert.Equal(expected, options.WeightIndicator.Decoding.DummyZero);
    }
}
