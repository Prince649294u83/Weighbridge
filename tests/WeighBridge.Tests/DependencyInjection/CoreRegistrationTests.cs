using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WeighBridge.Core.DependencyInjection;
using WeighBridge.Core.Health;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Notifications;
using WeighBridge.Core.Tasks;
using WeighBridge.Services.DependencyInjection;
using WeighBridge.Services.Health;
using WeighBridge.Services.Notifications;
using WeighBridge.Services.Tasks;
using Xunit;

namespace WeighBridge.Tests.DependencyInjection;

/// <summary>
/// Proves the infrastructure added in Phase 0.5 is registered and resolvable.
/// </summary>
/// <remarks>
/// The shell validates the whole container on build, but that only fails at launch. These
/// resolve the Core-layer registrations directly, so a missing one fails in the test run
/// instead of on an operator's terminal.
/// </remarks>
public sealed class CoreRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddWeighBridgeCore(new ConfigurationBuilder().Build());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Theory]
    [InlineData(typeof(ApplicationLogger))]
    [InlineData(typeof(AuditLogger))]
    [InlineData(typeof(HardwareLogger))]
    [InlineData(typeof(DatabaseLogger))]
    [InlineData(typeof(UIInteractionLogger))]
    [InlineData(typeof(IApplicationLogger))]
    [InlineData(typeof(IAuditLogger))]
    public void CategoryLogger_Resolves(Type serviceType)
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService(serviceType));
    }

    [Fact]
    public void CategoryLoggers_AreSingletons()
    {
        using var provider = BuildProvider();

        Assert.Same(
            provider.GetRequiredService<ApplicationLogger>(),
            provider.GetRequiredService<ApplicationLogger>());

        // The interface must hand back the same instance as the concrete type, not a
        // second one — two loggers would be harmless, but two of anything registered this
        // way is a sign the delegating registration was written as AddSingleton<I, T>().
        Assert.Same(
            provider.GetRequiredService<ApplicationLogger>(),
            provider.GetRequiredService<IApplicationLogger>());

        Assert.Same(
            provider.GetRequiredService<AuditLogger>(),
            provider.GetRequiredService<IAuditLogger>());
    }

    [Fact]
    public void EachCategoryLogger_WritesToItsOwnCategory()
    {
        using var provider = BuildProvider();

        Assert.Equal(LogCategory.Application, provider.GetRequiredService<ApplicationLogger>().Category);
        Assert.Equal(LogCategory.Audit, provider.GetRequiredService<AuditLogger>().Category);
        Assert.Equal(LogCategory.Hardware, provider.GetRequiredService<HardwareLogger>().Category);
        Assert.Equal(LogCategory.Database, provider.GetRequiredService<DatabaseLogger>().Category);
        Assert.Equal(LogCategory.UserInterface, provider.GetRequiredService<UIInteractionLogger>().Category);
    }

    [Fact]
    public void NotificationOptions_BindDefaultsWhenTheSectionIsAbsent()
    {
        using var provider = BuildProvider();

        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<NotificationOptions>>().Value;

        Assert.Equal(5, options.MaxActive);
        Assert.Equal(200, options.MaxHistory);
        Assert.True(options.ProtectErrorsFromEviction);
    }

    [Fact]
    public void NotificationOptions_BindFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:MaxActive"] = "3",
                ["Notifications:MaxHistory"] = "50",
                ["Notifications:ProtectErrorsFromEviction"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWeighBridgeCore(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<NotificationOptions>>().Value;

        Assert.Equal(3, options.MaxActive);
        Assert.Equal(50, options.MaxHistory);
        Assert.False(options.ProtectErrorsFromEviction);
    }

    [Fact]
    public void NotificationService_ResolvesAsTheSameSingletonAsTheManager()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddWeighBridgeCore(new ConfigurationBuilder().Build());
        services.AddWeighBridgeServices();

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<NotificationManager>(),
            provider.GetRequiredService<INotificationService>());
    }

    [Fact]
    public void BackgroundTaskManager_ResolvesAsASingletonUnderBothItsTypes()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddWeighBridgeCore(new ConfigurationBuilder().Build());
        services.AddWeighBridgeServices();

        using var provider = services.BuildServiceProvider();

        // One instance, or a second one would own running loops that shutdown never sees.
        Assert.Same(
            provider.GetRequiredService<BackgroundTaskManager>(),
            provider.GetRequiredService<IBackgroundTaskManager>());
    }

    [Fact]
    public void BackgroundTaskManagerOptions_BindFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BackgroundTasks:MaxConcurrentTasks"] = "4",
                ["BackgroundTasks:ShutdownTimeoutSeconds"] = "20",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWeighBridgeCore(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<BackgroundTaskManagerOptions>>().Value;

        Assert.Equal(4, options.MaxConcurrentTasks);
        Assert.Equal(TimeSpan.FromSeconds(20), options.ShutdownTimeout);
    }

    [Fact]
    public void HealthMonitor_ResolvesAsASingletonUnderBothItsTypes()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddWeighBridgeCore(new ConfigurationBuilder().Build());
        services.AddWeighBridgeServices();

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<HealthMonitorService>(),
            provider.GetRequiredService<IHealthMonitor>());
    }

    [Fact]
    public void HealthRefreshTask_ResolvesAndTakesItsIntervalFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HealthMonitoring:IntervalSeconds"] = "45",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWeighBridgeCore(configuration);
        services.AddWeighBridgeServices();

        using var provider = services.BuildServiceProvider();

        var task = provider.GetRequiredService<HealthRefreshTask>();

        Assert.Equal(TimeSpan.FromSeconds(45), task.Interval);
    }

    [Fact]
    public void HealthMonitorOptions_FloorTheIntervalSoABadValueCannotBusyLoop()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HealthMonitoring:IntervalSeconds"] = "0",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWeighBridgeCore(configuration);

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<
            Microsoft.Extensions.Options.IOptions<HealthMonitorOptions>>().Value;

        // A zero or negative interval would make the recurring task hammer every serial
        // port and network share the moment someone mistypes the setting.
        Assert.Equal(TimeSpan.FromSeconds(5), options.Interval);
    }
}
