using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Busy;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Events;
using WeighBridge.Core.Health;
using WeighBridge.Core.Messaging;
using WeighBridge.Core.Navigation;
using WeighBridge.Core.Notifications;
using WeighBridge.Core.Security;
using WeighBridge.Core.Status;
using WeighBridge.Core.Tasks;
using WeighBridge.Core.Threading;
using WeighBridge.Core.Undo;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Services.Busy;
using WeighBridge.Services.Commands;
using WeighBridge.Services.Events;
using WeighBridge.Services.Health;
using WeighBridge.Services.Masters;
using WeighBridge.Services.Messaging;
using WeighBridge.Services.Navigation;
using WeighBridge.Services.Notifications;
using WeighBridge.Services.Security;
using WeighBridge.Services.Status;
using WeighBridge.Services.Tasks;
using WeighBridge.Services.Undo;
using WeighBridge.Services.Weighments;

namespace WeighBridge.Services.DependencyInjection;

/// <summary>Registers the application service layer.</summary>
public static class ServicesServiceCollectionExtensions
{
    /// <summary>
    /// Adds navigation, the event bus, server connectivity, the notification centre, the
    /// background task manager, the health monitor and the aggregated system status service.
    /// </summary>
    public static IServiceCollection AddWeighBridgeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Fallback only. The WPF host registers its Dispatcher-backed implementation
        // first, and TryAdd leaves it in place; a headless host gets the inline one.
        services.TryAddSingleton<IUiDispatcher, ImmediateUiDispatcher>();

        // One bus for the whole application, exposed under three interfaces so a
        // component's constructor can declare whether it publishes, subscribes or both.
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventBus>(provider => provider.GetRequiredService<EventBus>());
        services.AddSingleton<IEventPublisher>(provider => provider.GetRequiredService<EventBus>());
        services.AddSingleton<IEventSubscriber>(provider => provider.GetRequiredService<EventBus>());

        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IServerConnectivityService, ServerConnectivityService>();

        // One notification centre for the whole application: the history has to outlive
        // any single page, so a scoped or transient lifetime would lose it on navigation.
        services.AddSingleton<NotificationManager>();
        services.AddSingleton<INotificationService>(provider => provider.GetRequiredService<NotificationManager>());

        // One task manager, for the same reason and a stronger one: it owns the running
        // loops, and a second instance would mean work nobody can stop at shutdown.
        services.AddSingleton<BackgroundTaskManager>();
        services.AddSingleton<IBackgroundTaskManager>(provider => provider.GetRequiredService<BackgroundTaskManager>());

        // One busy state. Two instances would each believe the application was idle while
        // the other held an operation, and the indicator would flicker between them.
        services.AddSingleton<BusyStateService>();
        services.AddSingleton<IBusyStateService>(provider => provider.GetRequiredService<BusyStateService>());

        // One undo history per application. It is scoped by the operator's current context,
        // not by module, so a weighment started on one page can be corrected from another.
        services.AddSingleton<UndoManager>();
        services.AddSingleton<IUndoManager>(provider => provider.GetRequiredService<UndoManager>());

        // One permission state.
        services.AddSingleton<PermissionService>();
        services.AddSingleton<IPermissionService>(provider => provider.GetRequiredService<PermissionService>());

        // Authentication service
        services.AddSingleton<IAuthenticationService, AuthenticationService>();

        // Anti-Piracy Hardware Locking & RSA-2048 Licensing
        services.AddSingleton<IHardwareIdProvider, HardwareIdProvider>();
        services.AddSingleton<ILicenseService, RsaLicenseService>();

        // The command pipeline. A singleton because it is stateless per execution.
        services.AddSingleton<ICommandExecutor, CommandExecutor>();

        // One health monitor: a subsystem's availability is a single fact, and two monitors
        // probing the same serial port would both be wrong about it.
        services.AddSingleton<HealthMonitorService>();
        services.AddSingleton<IHealthMonitor>(provider => provider.GetRequiredService<HealthMonitorService>());

        // The monitor's timer, as a background task so shutdown, restart and the
        // concurrency cap are the manager's job rather than the monitor's. Registering it
        // with the manager and starting it is the host's call, not this method's.
        services.AddSingleton<HealthRefreshTask>();

        // Date & Time formatting service
        services.AddSingleton<IDateTimeFormatter, WeighBridge.Services.Formatting.DateTimeFormatter>();

        // Business services.
        services.AddSingleton<Func<IUnitOfWork>>(provider => provider.GetRequiredService<IUnitOfWork>);
        services.AddSingleton<IWeighmentService, WeighmentService>();
        services.AddSingleton<IVehicleTypeService, VehicleTypeService>();
        services.AddSingleton<IVehicleService, VehicleService>();
        services.AddSingleton<IPartyService, PartyService>();
        services.AddSingleton<IMaterialService, MaterialService>();

        // SMS Messaging Subsystem
        services.TryAddSingleton<HttpClient>();
        services.AddSingleton<HttpGatewaySmsProvider>();
        services.AddSingleton<SmsTemplateEngine>();
        services.AddSingleton<SmsRecipientResolver>();
        services.AddSingleton<ISmsProvider, GsmModemSmsProvider>();
        services.AddSingleton<ISmsProvider>(provider => provider.GetRequiredService<HttpGatewaySmsProvider>());
        services.AddSingleton<ISmsService, SmsOutboxProcessor>();
        services.AddSingleton<SmsOutboxTask>();

        // Email Messaging Subsystem
        services.AddSingleton<IEmailService, SmtpEmailService>();

        // Legacy Data Migration
        services.AddSingleton<ILegacyDataImporter, WeighBridge.Services.Import.MdbLegacyImporter>();

        // SystemStatusService needs the database probe specifically; resolving
        // IHealthCheck by interface would be ambiguous once other checks register.
        services.AddSingleton<ISystemStatusService>(provider => new SystemStatusService(
            provider.GetRequiredService<DatabaseHealthCheck>(),
            provider.GetRequiredService<IWeightIndicatorService>(),
            provider.GetRequiredService<IPrintService>(),
            provider.GetRequiredService<IServerConnectivityService>(),
            provider.GetRequiredService<IOptions<DatabaseOptions>>(),
            provider.GetRequiredService<ILogger<SystemStatusService>>(),
            provider.GetRequiredService<IUiDispatcher>()));

        return services;
    }
}
