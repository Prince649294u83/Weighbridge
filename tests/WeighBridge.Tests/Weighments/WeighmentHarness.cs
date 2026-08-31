using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Notifications;
using WeighBridge.Core.Security;
using WeighBridge.Core.Undo;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Services.Busy;
using WeighBridge.Services.Commands;
using WeighBridge.Services.Events;
using WeighBridge.Services.Notifications;
using WeighBridge.Services.Security;
using WeighBridge.Services.Undo;
using WeighBridge.Services.Weighments;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Weighments;

/// <summary>
/// A real SQLite file with the real migration applied, the real service over it, and the real
/// command pipeline over that.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not an in-memory provider. The value converter that stores kilograms as
/// integer grams, the unique index on the slip number and the two-phase insert that derives
/// it from the allocated identity are all things the in-memory provider does not have, so a
/// test against it would pass while the shipped database failed.
/// </para>
/// <para>
/// The unit of work is handed out through a factory exactly as the container does it: a fresh
/// <see cref="WeighBridgeDbContext"/> per operation, so a test that reloads a row gets what
/// the database holds rather than what a change tracker remembers.
/// </para>
/// </remarks>
internal sealed class WeighmentHarness : IDisposable
{
    private readonly TempDataRoot _root = new();
    private readonly DbContextOptions<WeighBridgeDbContext> _options;

    public WeighmentHarness(IOptions<WeighmentOptions>? weighmentOptions = null)
    {
        _options = MigratedDatabase(_root);

        var applicationInfo = new TestApplicationInfoService();
        var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace));
        var dispatcher = new TestUiDispatcher();
        var applicationLogger = new ApplicationLogger(factory, applicationInfo);

        Events = new EventBus(dispatcher, factory.CreateLogger<EventBus>());

        Operator = new SignedInOperator();
        Permissions = new PermissionService(
            applicationInfo,
            applicationLogger,
            Operator);

        // Harnesses execute commands as an administrator; the service itself starts
        // unauthenticated, exactly as production does.
        Permissions.SetOperator(new OperatorIdentity("Test", "Test", Roles.Administrator));

        Service = new WeighmentService(
            () => new UnitOfWork(CreateContext(), Operator),
            Permissions,
            Events,
            factory.CreateLogger<WeighmentService>(),
            weighmentOptions);

        Undo = new UndoManager(Options.Create(new UndoOptions { MaxDepth = 20 }), applicationLogger);

        Executor = new CommandExecutor(
            Permissions,
            new BusyStateService(dispatcher, applicationLogger),
            Undo,
            Events,
            new NotificationManager(
                Events,
                dispatcher,
                Options.Create(new NotificationOptions()),
                factory.CreateLogger<NotificationManager>()),
            new AuditLogger(factory, applicationInfo),
            new UIInteractionLogger(factory, applicationInfo));
    }

    public EventBus Events { get; }

    public PermissionService Permissions { get; }

    public IWeighmentService Service { get; }

    public UndoManager Undo { get; }

    public CommandExecutor Executor { get; }

    /// <summary>Provisions an empty database with the migration applied, as startup does.</summary>
    public static DbContextOptions<WeighBridgeDbContext> MigratedDatabase(TempDataRoot root)
    {
        Directory.CreateDirectory(root.Root);

        var options = new DbContextOptionsBuilder<WeighBridgeDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root.Root, "weighbridge.db")}")
            .Options;

        // Migrate, not EnsureCreated: the schema under test has to be the schema that ships.
        using var context = new WeighBridgeDbContext(options);
        context.Database.Migrate();

        return options;
    }

    /// <summary>A context on the same file, with an empty change tracker.</summary>
    public SignedInOperator Operator { get; }

    public WeighBridgeDbContext CreateContext() => new(_options);

    public void SignInAs(Role role)
        => Permissions.SetOperator(new OperatorIdentity("tester", "Tester", role));

    public void Dispose()
    {
        // SQLite pools the connection, and the temp directory cannot be removed while the
        // file handle is open.
        SqliteConnection.ClearAllPools();
        _root.Dispose();
    }
}
