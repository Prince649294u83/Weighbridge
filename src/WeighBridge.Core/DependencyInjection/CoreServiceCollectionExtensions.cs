using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WeighBridge.Core.Application;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Health;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Notifications;
using WeighBridge.Core.Security;
using WeighBridge.Core.Tasks;
using WeighBridge.Core.Undo;

namespace WeighBridge.Core.DependencyInjection;

/// <summary>
/// Registers the cross-cutting kernel: strongly typed options and application
/// identity services.
/// </summary>
public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Binds every configuration section to its options type and registers the
    /// services that belong to the Core layer.
    /// </summary>
    public static IServiceCollection AddWeighBridgeCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ApplicationOptions>()
            .Bind(configuration.GetSection(ApplicationOptions.SectionName));

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName));

        services.AddOptions<HardwareOptions>()
            .Configure(options =>
            {
                var section = configuration.GetSection(HardwareOptions.SectionName);

                // Normalise DummyZero before binding: older config files may store it
                // as a JSON boolean ("false"/"true") instead of integer 0/1. The
                // Microsoft.Extensions.Configuration binder throws FormatException
                // when converting "False" to Int32, crashing startup.
                var dummyZeroRaw = section["WeightIndicator:Decoding:DummyZero"];
                bool dummyZeroWasBool = dummyZeroRaw != null
                    && bool.TryParse(dummyZeroRaw, out _);

                if (dummyZeroWasBool)
                {
                    // Temporarily null it out so Bind doesn't choke on it
                    section["WeightIndicator:Decoding:DummyZero"] = null;
                }

                section.Bind(options);

                // Restore the correct integer value
                if (dummyZeroWasBool)
                {
                    options.WeightIndicator.Decoding.DummyZero =
                        string.Equals(dummyZeroRaw, "true", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                }
            });

        services.AddOptions<CameraOptions>()
            .Bind(configuration.GetSection(CameraOptions.SectionName));

        services.AddOptions<PrinterOptions>()
            .Bind(configuration.GetSection(PrinterOptions.SectionName));

        services.AddOptions<ServerOptions>()
            .Bind(configuration.GetSection(ServerOptions.SectionName));

        services.AddOptions<ReportingOptions>()
            .Bind(configuration.GetSection(ReportingOptions.SectionName));

        services.AddOptions<FileLoggingOptions>()
            .Bind(configuration.GetSection(FileLoggingOptions.SectionName));

        services.AddOptions<WeighmentOptions>()
            .Bind(configuration.GetSection(WeighmentOptions.SectionName));

        services.AddOptions<CompanyOptions>()
            .Bind(configuration.GetSection(CompanyOptions.SectionName));

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName));

        services.AddOptions<SmsOptions>()
            .Bind(configuration.GetSection(SmsOptions.SectionName));

        // Bound even though appsettings.json carries no Notifications section yet: an
        // absent section leaves the defaults in place, and adding the section later needs
        // no code change here.
        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName));

        // Same reasoning: a site that needs a longer shutdown budget, or fewer concurrent
        // tasks on a slower terminal, changes configuration rather than code.
        services.AddOptions<BackgroundTaskManagerOptions>()
            .Bind(configuration.GetSection(BackgroundTaskManagerOptions.SectionName));

        // Likewise for how often subsystems are re-probed, which a site with a slow network
        // share may want to relax.
        services.AddOptions<HealthMonitorOptions>()
            .Bind(configuration.GetSection(HealthMonitorOptions.SectionName));

        // How deep the undo history goes. A site doing bulk corrections may want more than
        // the default twenty; a shared terminal may want fewer.
        services.AddOptions<UndoOptions>()
            .Bind(configuration.GetSection(UndoOptions.SectionName));

        // Which role an operator gets before there is a login system. Configuration rather
        // than code, so a site can set Operator today and have the permission checks bite.
        services.AddOptions<SecurityOptions>()
            .Bind(configuration.GetSection(SecurityOptions.SectionName));

        services.AddSingleton<IApplicationInfoService, ApplicationInfoService>();

        // Who is signed in, for the log enrichment. Registered before the loggers because
        // every one of them takes it: the permission service publishes the operator here
        // rather than the loggers asking for it, which would be a dependency cycle.
        services.AddSingleton<SignedInOperator>();

        // Category loggers. Singletons because they are stateless over a cached ILogger,
        // and registered as concrete types so a constructor names the category it writes
        // to; IApplicationLogger and IAuditLogger resolve to the general and audit ones.
        services.AddSingleton<ApplicationLogger>();
        services.AddSingleton<AuditLogger>();
        services.AddSingleton<HardwareLogger>();
        services.AddSingleton<DatabaseLogger>();
        services.AddSingleton<UIInteractionLogger>();

        services.AddSingleton<IApplicationLogger>(provider => provider.GetRequiredService<ApplicationLogger>());
        services.AddSingleton<IAuditLogger>(provider => provider.GetRequiredService<AuditLogger>());

        return services;
    }
}
