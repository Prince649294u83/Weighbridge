using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Logging;
using WeighBridge.Core.Notifications;
using WeighBridge.Core.Security;
using WeighBridge.Core.Undo;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Services.Busy;
using WeighBridge.Services.Commands;
using WeighBridge.Services.Events;
using WeighBridge.Services.Masters;
using WeighBridge.Services.Notifications;
using WeighBridge.Services.Security;
using WeighBridge.Services.Undo;
using WeighBridge.Services.Weighments;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Masters;

internal sealed class MasterHarness : IDisposable
{
    private readonly TempDataRoot _root = new();
    private readonly DbContextOptions<WeighBridgeDbContext> _options;

    public MasterHarness()
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

        VehicleTypeService = new VehicleTypeService(
            () => new UnitOfWork(CreateContext(), Operator),
            Permissions,
            Events,
            factory.CreateLogger<VehicleTypeService>());

        VehicleService = new VehicleService(
            () => new UnitOfWork(CreateContext(), Operator),
            Permissions,
            Events,
            factory.CreateLogger<VehicleService>());

        PartyService = new PartyService(
            () => new UnitOfWork(CreateContext(), Operator),
            Permissions,
            Events,
            factory.CreateLogger<PartyService>());

        MaterialService = new MaterialService(
            () => new UnitOfWork(CreateContext(), Operator),
            Permissions,
            Events,
            factory.CreateLogger<MaterialService>());

        WeighmentService = new WeighmentService(
            () => new UnitOfWork(CreateContext(), Operator),
            Permissions,
            Events,
            factory.CreateLogger<WeighmentService>());

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
    public IVehicleTypeService VehicleTypeService { get; }
    public IVehicleService VehicleService { get; }
    public IPartyService PartyService { get; }
    public IMaterialService MaterialService { get; }
    public IWeighmentService WeighmentService { get; }
    public UndoManager Undo { get; }
    public CommandExecutor Executor { get; }

    public static DbContextOptions<WeighBridgeDbContext> MigratedDatabase(TempDataRoot root)
    {
        Directory.CreateDirectory(root.Root);

        var options = new DbContextOptionsBuilder<WeighBridgeDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root.Root, "weighbridge.db")}")
            .Options;

        using var context = new WeighBridgeDbContext(options);
        context.Database.Migrate();

        return options;
    }

    public SignedInOperator Operator { get; }

        public WeighBridgeDbContext CreateContext() => new(_options);

    public void SignInAs(Role role)
        => Permissions.SetOperator(new OperatorIdentity("tester", "Tester", role));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        _root.Dispose();
    }
}
