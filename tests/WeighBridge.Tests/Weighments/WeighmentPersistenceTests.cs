using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Tests.Infrastructure;

namespace WeighBridge.Tests.Weighments;

/// <summary>
/// The service and the database together, on a real SQLite file with the real migration.
/// </summary>
/// <remarks>
/// Every assertion about a saved value reads it back through a context that never saw the
/// write. A test that re-inspects the object it just handed to the service proves only that
/// C# assignment works.
/// </remarks>
public sealed class WeighmentPersistenceTests : IDisposable
{
    private readonly WeighmentHarness _harness = new();

    public WeighmentPersistenceTests() => _harness.SignInAs(Roles.Administrator);

    public void Dispose() => _harness.Dispose();

    private static NewWeighment Request(
        string vehicleNumber = "MH12AB1234",
        WeighmentMode mode = WeighmentMode.GrossFirst)
        => new() { VehicleNumber = vehicleNumber, Mode = mode };

    private async Task<Weighment> Reload(long id)
    {
        await using var context = _harness.CreateContext();
        return await context.Set<Weighment>().SingleAsync(weighment => weighment.Id == id);
    }

    [Fact]
    public async Task TheMigration_IsWhatStartupWillApply()
    {
        await using var context = _harness.CreateContext();

        // Startup branches on this count: with no migrations it calls EnsureCreated, with
        // migrations it calls Migrate. The first real table moved the application onto the
        // second branch, and that is the branch that has to hold from here on.
        Assert.Contains(
            context.Database.GetMigrations(),
            migration => migration.EndsWith("AddVehicleEntry", StringComparison.Ordinal));

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
    }

    /// <summary>
    /// The upgrade path every existing machine takes. Before this module the model mapped no
    /// tables, so startup took the <c>EnsureCreated</c> branch and left behind a database file
    /// with no tables and no migrations-history table. Migrating that has to work.
    /// </summary>
    [Fact]
    public async Task ADatabaseProvisionedBeforeTheFirstMigration_UpgradesRatherThanBreaking()
    {
        using var root = new TempDataRoot();
        Directory.CreateDirectory(root.Root);

        var path = Path.Combine(root.Root, "legacy.db");
        var connectionString = $"Data Source={path}";

        // What the old EnsureCreated branch produced: the file, and nothing in it.
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
        }

        Assert.True(File.Exists(path));

        var options = new DbContextOptionsBuilder<WeighBridgeDbContext>()
            .UseSqlite(connectionString)
            .Options;

        await using (var upgraded = new WeighBridgeDbContext(options))
        {
            await upgraded.Database.MigrateAsync();

            Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
            Assert.Equal(0, await upgraded.Set<Weighment>().CountAsync());
        }

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task Create_AllocatesTheFirstSlipNumber_AndTheRowSurvivesAReload()
    {
        var created = await _harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = " mh-12 ab 1234 ",
            Mode = WeighmentMode.TareFirst,
            PartyName = "Acme Cement",
            MaterialName = "Clinker",
            DriverName = "R. Singh",
            TransporterName = "Blue Line",
            Remarks = "Gate 2",
        });

        Assert.Equal("WB-000001", created.SlipNumber);
        Assert.False(created.IsTransient);

        var reloaded = await Reload(created.Id);

        Assert.Equal("WB-000001", reloaded.SlipNumber);
        Assert.Equal("MH12AB1234", reloaded.VehicleNumber);
        Assert.Equal(WeighmentMode.TareFirst, reloaded.Mode);
        Assert.Equal(WeighmentStatus.Created, reloaded.Status);
        Assert.Equal("Acme Cement", reloaded.PartyName);
        Assert.Equal("Clinker", reloaded.MaterialName);
        Assert.Equal("R. Singh", reloaded.DriverName);
        Assert.Equal("Blue Line", reloaded.TransporterName);
        Assert.Equal("Gate 2", reloaded.Remarks);
        Assert.Null(reloaded.FirstWeight);
        Assert.Null(reloaded.SecondWeight);
        Assert.Null(reloaded.NetWeightKg);
        Assert.False(reloaded.IsDeleted);
    }

    [Fact]
    public async Task Create_StampsWhoOpenedItAndWhen_ButNotAModification()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var created = await _harness.Service.CreateAsync(Request());
        var reloaded = await Reload(created.Id);

        Assert.Equal("tester", reloaded.CreatedBy);
        Assert.InRange(reloaded.CreatedAtUtc, before, DateTime.UtcNow.AddSeconds(1));

        // A brand-new weighment has not been edited. The slip number is written by the same
        // transaction that inserted the row, so it must not read as a later modification.
        Assert.Null(reloaded.ModifiedAtUtc);
        Assert.Null(reloaded.ModifiedBy);
    }

    [Fact]
    public async Task SlipNumbers_FollowTheOrderWeighmentsWereOpened()
    {
        var first = await _harness.Service.CreateAsync(Request("MH12AB0001"));
        var second = await _harness.Service.CreateAsync(Request("MH12AB0002"));
        var third = await _harness.Service.CreateAsync(Request("MH12AB0003"));

        Assert.Equal(["WB-000001", "WB-000002", "WB-000003"], new[] { first, second, third }.Select(w => w.SlipNumber));
    }

    [Fact]
    public async Task TwoWeighmentsWithTheSameSlipNumber_AreRefusedByTheDatabase()
    {
        var existing = await _harness.Service.CreateAsync(Request("MH12AB0001"));

        await using var context = _harness.CreateContext();
        var clash = Weighment.Open("MH12AB0002", WeighmentMode.GrossFirst);
        context.Add(clash);

        // The slip number has no public setter, so this is not something the service could
        // do by accident. The unique index is the guarantee that it cannot happen by any
        // other route either — a second slip claiming to be WB-000001 makes both worthless.
        context.Entry(clash).Property<string>(nameof(Weighment.SlipNumber)).CurrentValue = existing.SlipNumber;

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task TheFullCycle_PersistsGrossTareAndNet()
    {
        var created = await _harness.Service.CreateAsync(Request(mode: WeighmentMode.GrossFirst));

        await _harness.Service.RecordFirstWeightAsync(created.Id, 32_500m, WeightSource.Indicator);

        var afterFirst = await Reload(created.Id);
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, afterFirst.Status);
        Assert.Equal(32_500m, afterFirst.FirstWeight!.Kilograms);
        Assert.Equal(WeightSource.Indicator, afterFirst.FirstWeight.Source);
        Assert.NotNull(afterFirst.ModifiedBy);
        Assert.Null(afterFirst.NetWeightKg);

        await _harness.Service.RecordSecondWeightAsync(created.Id, 12_250.5m, WeightSource.Manual);

        var completed = await Reload(created.Id);
        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(32_500m, completed.Gross!.Kilograms);
        Assert.Equal(12_250.5m, completed.Tare!.Kilograms);
        Assert.Equal(20_249.5m, completed.NetWeightKg);
        Assert.Equal(WeightSource.Manual, completed.SecondWeight!.Source);
        Assert.True(completed.SecondWeight.IsManual);
        Assert.NotNull(completed.CompletedAtUtc);
        Assert.Equal("tester", completed.ModifiedBy);
        Assert.Equal("Print slip", completed.NextAction);
    }

    /// <summary>
    /// The storage decision, verified at the column rather than through the model that made
    /// it. SQLite has no decimal type; a weight kept as text sorts lexicographically and a
    /// weight kept as REAL stops being exact.
    /// </summary>
    [Fact]
    public async Task Weights_AreStoredAsWholeGrams()
    {
        var created = await _harness.Service.CreateAsync(Request());
        await _harness.Service.RecordFirstWeightAsync(created.Id, 12_250.5m, WeightSource.Indicator);

        await using var context = _harness.CreateContext();
        var grams = await context.Database
            .SqlQueryRaw<long>("SELECT FirstWeightGrams AS Value FROM Weighments WHERE Id = {0}", created.Id)
            .SingleAsync();

        Assert.Equal(12_250_500L, grams);
    }

    [Fact]
    public async Task NetWeight_ComparesAndOrdersNumericallyInSql()
    {
        await Complete("MH12AB0001", gross: 21_000m, tare: 12_000m); // net 9,000
        await Complete("MH12AB0002", gross: 24_000m, tare: 12_000m); // net 12,000

        await using var context = _harness.CreateContext();

        // Stored as text, "9000" would sort after "12000" and be counted as the heavier load.
        var heavy = await context.Set<Weighment>()
            .Where(weighment => weighment.NetWeightKg > 10_000m)
            .Select(weighment => weighment.VehicleNumber)
            .ToListAsync();

        Assert.Equal(["MH12AB0002"], heavy);

        var ordered = await context.Set<Weighment>()
            .OrderByDescending(weighment => weighment.NetWeightKg)
            .Select(weighment => weighment.NetWeightKg)
            .ToListAsync();

        Assert.Equal([12_000m, 9_000m], ordered);
    }

    [Fact]
    public async Task Cancel_KeepsTheRowAndTheReason()
    {
        var created = await _harness.Service.CreateAsync(Request());
        await _harness.Service.RecordFirstWeightAsync(created.Id, 32_500m, WeightSource.Indicator);

        await _harness.Service.CancelAsync(created.Id, "Driver left without the second weighing");

        var reloaded = await Reload(created.Id);
        Assert.Equal(WeighmentStatus.Cancelled, reloaded.Status);
        Assert.Equal("Driver left without the second weighing", reloaded.CancellationReason);
        Assert.NotNull(reloaded.CancelledAtUtc);
        Assert.Equal(32_500m, reloaded.FirstWeight!.Kilograms);
    }

    [Fact]
    public async Task AwaitingSecondWeight_ListsOnlyTheVehiclesExpectedBack()
    {
        var waiting = await _harness.Service.CreateAsync(Request("MH12AB0001"));
        await _harness.Service.RecordFirstWeightAsync(waiting.Id, 32_500m, WeightSource.Indicator);

        await _harness.Service.CreateAsync(Request("MH12AB0002")); // no weight yet
        await Complete("MH12AB0003", gross: 30_000m, tare: 12_000m); // finished

        var cancelled = await _harness.Service.CreateAsync(Request("MH12AB0004"));
        await _harness.Service.RecordFirstWeightAsync(cancelled.Id, 28_000m, WeightSource.Indicator);
        await _harness.Service.CancelAsync(cancelled.Id, "Driver left");

        var awaiting = await _harness.Service.GetAwaitingSecondWeightAsync();

        Assert.Equal(["MH12AB0001"], awaiting.Select(weighment => weighment.VehicleNumber));
    }

    [Fact]
    public async Task GetRecent_ReturnsTheNewestFirst_AndNoMoreThanAsked()
    {
        for (var index = 1; index <= 4; index++)
        {
            await _harness.Service.CreateAsync(Request($"MH12AB000{index}"));
        }

        var recent = await _harness.Service.GetRecentAsync(count: 2);

        Assert.Equal(["MH12AB0004", "MH12AB0003"], recent.Select(weighment => weighment.VehicleNumber));
    }

    [Theory]
    [InlineData("WB-000001")]
    [InlineData("wb-1")]
    [InlineData("  1  ")]
    public async Task GetBySlipNumber_FindsWhatTheOperatorTyped(string typed)
    {
        var created = await _harness.Service.CreateAsync(Request());

        var found = await _harness.Service.GetBySlipNumberAsync(typed);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found!.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a slip")]
    [InlineData("WB-000999")]
    public async Task GetBySlipNumber_ReturnsNothingRatherThanGuessing(string typed)
    {
        await _harness.Service.CreateAsync(Request());

        Assert.Null(await _harness.Service.GetBySlipNumberAsync(typed));
    }

    /// <summary>
    /// Retired weighments stay in the table for the audit trail and disappear from every
    /// query, so no caller has to remember to exclude them.
    /// </summary>
    [Fact]
    public async Task ARetiredWeighment_LeavesTheListsButNotTheTable()
    {
        var created = await _harness.Service.CreateAsync(Request());
        await _harness.Service.RecordFirstWeightAsync(created.Id, 32_500m, WeightSource.Indicator);

        await using (var context = _harness.CreateContext())
        {
            var row = await context.Set<Weighment>().SingleAsync(weighment => weighment.Id == created.Id);
            row.IsDeleted = true;
            await context.SaveChangesAsync();
        }

        Assert.Empty(await _harness.Service.GetAwaitingSecondWeightAsync());
        Assert.Empty(await _harness.Service.GetRecentAsync());

        await using var audit = _harness.CreateContext();
        Assert.Equal(1, await audit.Set<Weighment>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Create_PublishesTheOpenedFact()
    {
        WeighmentCreatedEvent? published = null;
        using var subscription = _harness.Events.Subscribe<WeighmentCreatedEvent>(e => published = e);

        var created = await _harness.Service.CreateAsync(Request(mode: WeighmentMode.TareFirst));

        Assert.NotNull(published);
        Assert.Equal(created.Id, published!.WeighmentId);
        Assert.Equal("WB-000001", published.SlipNumber);
        Assert.Equal("MH12AB1234", published.VehicleNumber);
        Assert.Equal(WeighmentMode.TareFirst, published.Mode);
        Assert.Equal("VehicleEntry", published.Source);
    }

    [Fact]
    public async Task RecordFirstWeight_PublishesTheWeighing()
    {
        FirstWeightRecordedEvent? published = null;
        using var subscription = _harness.Events.Subscribe<FirstWeightRecordedEvent>(e => published = e);

        var created = await _harness.Service.CreateAsync(Request());
        await _harness.Service.RecordFirstWeightAsync(created.Id, 32_500m, WeightSource.Manual);

        Assert.NotNull(published);
        Assert.Equal(32_500m, published!.Kilograms);
        Assert.Equal(WeightSource.Manual, published.WeightSource);
    }

    /// <summary>
    /// Two events for one call, because they are two facts: a yard display cares that a
    /// vehicle was weighed, the printer and the reports care that a transaction closed.
    /// </summary>
    [Fact]
    public async Task RecordSecondWeight_PublishesBothTheWeighingAndTheCompletion()
    {
        SecondWeightRecordedEvent? weighed = null;
        WeighmentCompletedEvent? completed = null;
        using var weighing = _harness.Events.Subscribe<SecondWeightRecordedEvent>(e => weighed = e);
        using var completion = _harness.Events.Subscribe<WeighmentCompletedEvent>(e => completed = e);

        var created = await _harness.Service.CreateAsync(Request());
        await _harness.Service.RecordFirstWeightAsync(created.Id, 32_500m, WeightSource.Indicator);
        await _harness.Service.RecordSecondWeightAsync(created.Id, 12_250.5m, WeightSource.Indicator);

        Assert.NotNull(weighed);
        Assert.Equal(12_250.5m, weighed!.Kilograms);

        Assert.NotNull(completed);
        Assert.Equal(32_500m, completed!.GrossKilograms);
        Assert.Equal(12_250.5m, completed.TareKilograms);
        Assert.Equal(20_249.5m, completed.NetKilograms);
        Assert.Equal(completed.GrossKilograms - completed.TareKilograms, completed.NetKilograms);
    }

    [Fact]
    public async Task Cancel_PublishesTheReasonAnAuditorWillAskAbout()
    {
        WeighmentCancelledEvent? published = null;
        using var subscription = _harness.Events.Subscribe<WeighmentCancelledEvent>(e => published = e);

        var created = await _harness.Service.CreateAsync(Request());
        await _harness.Service.CancelAsync(created.Id, "Wrong vehicle on the platform");

        Assert.NotNull(published);
        Assert.Equal("Wrong vehicle on the platform", published!.Reason);
        Assert.Equal("WB-000001", published.SlipNumber);
    }

    /// <summary>
    /// Nothing is published when nothing happened. A subscriber that printed a slip for a
    /// weighment that failed to save would be acting on a fact that does not exist.
    /// </summary>
    [Fact]
    public async Task ARefusedTransition_PublishesNothing()
    {
        var events = 0;
        using var subscription = _harness.Events.Subscribe<SecondWeightRecordedEvent>(_ => events++);

        var created = await _harness.Service.CreateAsync(Request());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _harness.Service.RecordSecondWeightAsync(created.Id, 12_000m, WeightSource.Indicator));

        Assert.Equal(0, events);

        var reloaded = await Reload(created.Id);
        Assert.Equal(WeighmentStatus.Created, reloaded.Status);
        Assert.Null(reloaded.SecondWeight);
    }

    [Fact]
    public async Task AWeighmentThatDoesNotExist_IsRefusedWithSomethingReadable()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _harness.Service.RecordFirstWeightAsync(4_242L, 32_500m, WeightSource.Indicator));

        Assert.Contains("4242", error.Message, StringComparison.Ordinal);
        Assert.Null(await _harness.Service.GetAsync(4_242L));
    }

    private async Task<Weighment> Complete(string vehicleNumber, decimal gross, decimal tare)
    {
        var created = await _harness.Service.CreateAsync(Request(vehicleNumber));
        await _harness.Service.RecordFirstWeightAsync(created.Id, gross, WeightSource.Indicator);
        return await _harness.Service.RecordSecondWeightAsync(created.Id, tare, WeightSource.Indicator);
    }
}
