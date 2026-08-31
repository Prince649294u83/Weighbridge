using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.App.Dialogs;
using WeighBridge.App.Navigation;
using WeighBridge.App.Services;
using WeighBridge.App.ViewModels;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Application;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.DependencyInjection;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Settings;
using WeighBridge.Core.Status;
using WeighBridge.Core.Tasks;
using WeighBridge.Core.Theming;
using WeighBridge.Core.Threading;
using WeighBridge.Hardware.DependencyInjection;
using WeighBridge.Infrastructure.DependencyInjection;
using WeighBridge.Printing.DependencyInjection;
using WeighBridge.Reporting.DependencyInjection;
using WeighBridge.Services.DependencyInjection;
using WeighBridge.Services.Health;
using WeighBridge.Settings.Configuration;
using WeighBridge.Settings.DependencyInjection;

namespace WeighBridge.App;

/// <summary>
/// The application's composition root and startup pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Runs the startup sequence in a fixed order, because each step depends on the one
/// before it: ensure the data folders exist, provision <c>appsettings.json</c>, build
/// configuration from it, start logging, register services, load preferences, apply the
/// theme, then create the shell.
/// </para>
/// <para>
/// Nothing here resolves a service on demand from a static hook - the container is
/// private and every dependency is delivered through a constructor. The one exception is
/// <see cref="CreateShell"/>, which must ask the container for the root object; that is
/// what a composition root is for.
/// </para>
/// </remarks>
public sealed class Bootstrapper : IAsyncDisposable
{
    private readonly IApplicationPaths _paths = new ApplicationPaths();

    private ServiceProvider? _services;
    private FileLoggerProvider? _fileLoggerProvider;
    private ILogger<Bootstrapper>? _logger;

    /// <summary>The composed container. Available only after <see cref="StartAsync"/>.</summary>
    public IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("The bootstrapper has not been started yet.");

    /// <summary>Path of the log file this session is writing to, for diagnostics.</summary>
    public string? LogFilePath => _fileLoggerProvider?.CurrentLogFilePath;

    /// <summary>
    /// Builds configuration, logging and the container, then loads the state the shell
    /// needs before it can be shown.
    /// </summary>
    public async Task StartAsync()
    {
        // 1. Writable folders. Everything after this point may write to disk.
        _paths.EnsureCreated();

        // 2. Configuration file. Created from defaults on first run, repaired if a newer
        //    build introduced keys, regenerated if an operator's edit made it unreadable.
        //    Logging is not up yet, so the outcome is logged in step 4.
        var provisioner = new ConfigurationProvisioner(_paths);
        var configurationFile = provisioner.EnsureConfigurationFile();
        var provisioningOutcome = provisioner.LastOutcome;

        // 3. Configuration. Optional despite step 2: a file deleted between the two must
        //    still leave the application startable on the options classes' own defaults.
        //    Secrets (camera passwords, server API keys) are stored DPAPI-protected in the
        //    file and are decrypted into this view here, so no consumer ever sees the
        //    protected form.
        var rawConfiguration = new ConfigurationBuilder()
            .SetBasePath(_paths.DataRoot)
            .AddJsonFile(configurationFile, optional: true, reloadOnChange: true)
            .AddEnvironmentVariables("WEIGHBRIDGE_")
            .Build();

        var configuration = SecretAwareConfiguration.WithDecryptedSecrets(rawConfiguration);

        // 4. Container.
        var fileLoggingOptions = ReadFileLoggingOptions(configuration);

        var services = new ServiceCollection();
        ConfigureServices(services, configuration, fileLoggingOptions);

        _services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            // Catches a captive dependency - a singleton holding a transient - at
            // startup rather than as a mysterious stale value months later.
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        _fileLoggerProvider = _services.GetService<FileLoggerProvider>();
        _logger = _services.GetRequiredService<ILogger<Bootstrapper>>();

        // Fire-and-forget commands without a handler of their own report here, once:
        // logged for the record and surfaced to the operator instead of vanishing into
        // an unobserved task.
        var notifications = _services.GetService<Core.Notifications.INotificationService>();
        Core.Mvvm.AsyncRelayCommand.UnhandledExecutionError += ex =>
        {
            _logger.LogError(ex, "A screen action failed without its own error handler");
            notifications?.NotifyError(
                "Action failed",
                "Something went wrong on this screen. The log has the detail.");
        };

        var info = _services.GetRequiredService<IApplicationInfoService>();

        _logger.LogInformation(
            "==== {ApplicationName} {Version} starting on {Machine} ({OperatingSystem}) ====",
            info.ApplicationName,
            info.DisplayVersion,
            info.MachineName,
            info.OperatingSystem);

        _logger.LogInformation("Data root: {DataRoot}", _paths.DataRoot);
        _logger.LogInformation("Configuration file: {Path} ({Outcome})", configurationFile, provisioningOutcome);
        _logger.LogInformation("Log file: {Path}", LogFilePath ?? "(file logging disabled)");
        _logger.LogInformation("Dependency injection container built and validated");

        // 5. Preferences, before the theme: the theme choice lives in them.
        await _services.GetRequiredService<ISettingsService>().LoadAsync().ConfigureAwait(true);

        // 6. Theme.
        _services.GetRequiredService<IThemeService>().Initialize();

        _logger.LogInformation("Configuration, preferences and theme ready; awaiting database initialisation");

        // Deliberately no background work started yet. The health task probes the database,
        // and starting it here used to mean probing a database that had not been migrated —
        // noisy errors before login on a cold machine. StartBackgroundTasksAsync is the
        // caller's next step after InitializeDatabaseAsync succeeds.
    }

    /// <summary>
    /// Starts the infrastructure background loops once the services they probe exist in a
    /// usable state — after database initialisation, never before it.
    /// </summary>
    public void StartBackgroundTasks()
    {
        if (_services is null)
        {
            throw new InvalidOperationException("The bootstrapper has not been started yet.");
        }

        // Infrastructure background work. Registered and started here rather than by the
        // module that benefits from it, so every loop in the process has one owner and
        // shutdown has one place to stop them all.
        var tasks = _services.GetRequiredService<IBackgroundTaskManager>();
        tasks.Register(_services.GetRequiredService<HealthRefreshTask>());
        tasks.StartAll();

        _logger?.LogInformation("Background task manager started {Count} task(s)", tasks.Tasks.Count);
    }

    /// <summary>
    /// Resolves the shell and restores its persisted placement. The caller shows it.
    /// </summary>
    public MainWindow CreateShell()
    {
        var window = Services.GetRequiredService<MainWindow>();

        var placement = Services.GetRequiredService<IWindowPlacementService>();
        var options = Services.GetRequiredService<IOptions<ApplicationOptions>>().Value;

        if (options.RestoreWindowPlacement)
        {
            // Before the window is shown, so the operator never sees it move.
            placement.Attach(window);
        }

        _logger?.LogInformation("Application shell created");

        return window;
    }

    /// <summary>
    /// Prepares the database, and reports whether it is usable.
    /// </summary>
    /// <remarks>
    /// Awaited before the login dialog, because authentication queries the database. The
    /// caller decides what a failure means; returning the result rather than swallowing it
    /// is what lets startup stop with the database named, instead of continuing to a login
    /// dialog whose first query throws.
    /// </remarks>
    public async Task<DatabaseInitializationResult> InitializeDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var initializer = Services.GetRequiredService<IDatabaseInitializer>();
        var status = Services.GetRequiredService<ISystemStatusService>();

        var result = await initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            status.Database.Update(Domain.Enums.ConnectionState.Disconnected, result.Message);
            return result;
        }

        // Let the health check establish the state rather than assuming success here, so
        // the indicator and its tooltip come from a single source.
        await status.RefreshAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// Persists window placement and preferences, stops monitoring and flushes the log.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (_services is null)
        {
            return;
        }

        _logger?.LogInformation("Application shutting down");

        try
        {
            // Background loops first: they are the only things in the process that can still
            // touch the database or a serial port while the saves below are running.
            await _services.GetRequiredService<IBackgroundTaskManager>().StopAllAsync().ConfigureAwait(false);

            await _services.GetRequiredService<ISystemStatusService>().StopMonitoringAsync().ConfigureAwait(false);

            // Hardware next, and here rather than left to the container: disposing the
            // container is the last stage of shutdown and it takes the log writer with it,
            // so a serial port released there is released after the completion marker with
            // nothing able to record it.
            await _services.GetRequiredService<IWeightIndicatorService>().DisconnectAsync().ConfigureAwait(false);

            // WindowPlacementService captures on Closing but defers the write, so the
            // save has to happen here - after the window is gone, before the log closes.
            await _services.GetRequiredService<IWindowPlacementService>().SaveAsync().ConfigureAwait(false);
            await _services.GetRequiredService<ISettingsService>().SaveAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Shutdown must complete regardless; a failed save is logged, not thrown.
            _logger?.LogError(ex, "Shutdown housekeeping failed");
        }

        _logger?.LogInformation("==== Shutdown complete ====");

        // The audit writer's queue drains through its own Dispose, which the container
        // would call last — after the DbContext factory it needs was already disposed.
        // Disposing it explicitly here guarantees the final records reach the database
        // while every dependency is still alive.
        if (_services.GetService<AuditLogger>() is { } auditLogger)
        {
            auditLogger.Dispose();
        }

        _fileLoggerProvider?.Flush();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_services is null)
        {
            return;
        }

        // Disposes the container, which disposes the logger provider and with it the
        // file writer's background thread.
        await _services.DisposeAsync().ConfigureAwait(false);
        _services = null;
    }

    /// <summary>
    /// Registers every service in the application, layer by layer.
    /// </summary>
    private void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        FileLoggingOptions fileLoggingOptions)
    {
        services.AddSingleton(configuration);
        services.AddSingleton(_paths);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddWeighBridgeFileLogger(_paths.LogsDirectory, fileLoggingOptions);

            if (fileLoggingOptions.IncludeDebugOutput)
            {
                builder.AddDebug();
            }
        });

        // The UI-thread seam, registered before the service layer so its TryAdd fallback
        // does not win. Constructed here, on the UI thread, so it captures the right
        // dispatcher. Everything below the presentation layer marshals through this.
        services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher());

        // Library layers. Each owns its own registrations so a module never has to
        // know how another layer is composed.
        services.AddWeighBridgeCore(configuration);
        services.AddWeighBridgeInfrastructure();
        services.AddWeighBridgeSettings();
        services.AddWeighBridgeHardware();
        services.AddWeighBridgePrinting();
        services.AddWeighBridgeReporting();
        services.AddWeighBridgeServices();

        // Presentation services: the WPF-dependent implementations of Core abstractions.
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IWindowPlacementService, WindowPlacementService>();
        services.AddSingleton<IViewLocator, ViewLocator>();
        services.AddSingleton<SessionInactivityService>();

        // Shell. Transient, not singleton: signing out closes the shell and the next
        // sign-in gets a new one. The navigation rail is filtered by permission in the
        // ViewModel's constructor, so a shell held for the process lifetime would show the
        // first operator's modules to everyone who signed in after them.
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<MainWindow>();

        // Module ViewModels. Transient so revisiting a module starts it clean; the
        // navigation service builds them through the container on every navigation.
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<VehicleEntryViewModel>();
        services.AddTransient<DuplicateSlipViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<MastersViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AdministrationViewModel>();
        
        // Dialogs
        services.AddTransient<LoginDialogViewModel>();
    }

    /// <summary>
    /// Reads the file logging section directly.
    /// </summary>
    /// <remarks>
    /// The logger has to exist before the container does, so this one options object
    /// cannot come from <see cref="IOptions{TOptions}"/>. It is bound again through the
    /// normal options pipeline in <c>AddWeighBridgeCore</c> for everything else to use.
    /// </remarks>
    private static FileLoggingOptions ReadFileLoggingOptions(IConfiguration configuration)
    {
        var options = new FileLoggingOptions();
        configuration.GetSection(FileLoggingOptions.SectionName).Bind(options);
        return options;
    }
}
