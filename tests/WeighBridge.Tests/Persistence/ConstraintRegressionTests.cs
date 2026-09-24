using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Core.Abstractions;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Domain.Weighments;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Infrastructure.Repositories;
using WeighBridge.Tests.Infrastructure;
using WeighBridge.Tests.Masters;
using WeighBridge.Tests.Weighments;

namespace WeighBridge.Tests.Persistence;

/// <summary>
/// Regression pins for the schema hardening: the empty-slip collision, the
/// database-enforced one-open-weighment-per-vehicle rule, attribution stamps and
/// soft-delete/active consistency.
/// </summary>
public sealed class ConstraintRegressionTests
{
    private static NewWeighment Request(string vehicle = "MH12AB9999", WeighmentMode mode = WeighmentMode.GrossFirst) => new()
    {
        VehicleNumber = vehicle,
        Mode = mode,
    };

    /// <summary>
    /// The slip number is written in a second step after the identity is allocated. Two
    /// rows in that pre-numbered state used to collide on the unfiltered unique index and
    /// fail the whole batch; the filtered index lets both sit at "" until numbered.
    /// </summary>
    [Fact]
    public async Task TwoUnnumberedWeighments_InOneBatch_PersistWithoutCollision()
    {
        using var harness = new MasterHarness();

        await using var context = harness.CreateContext();
        context.Add(Weighment.Open("MH12AA0001", WeighmentMode.GrossFirst));
        context.Add(Weighment.Open("MH12AA0002", WeighmentMode.TareFirst));
        await context.SaveChangesAsync();

        Assert.Equal(2, await context.Set<Weighment>().CountAsync());
    }

    /// <summary>
    /// A crash between insert and numbering leaves a permanent "" row. That row must not
    /// block future inserts the way an unfiltered unique index did.
    /// </summary>
    [Fact]
    public async Task AnOrphanedUnnumberedRow_DoesNotBlockFutureWeighments()
    {
        using var harness = new MasterHarness();

        // Simulate the crash: a row inserted, never numbered.
        await using (var context = harness.CreateContext())
        {
            context.Add(Weighment.Open("MH12AA0010", WeighmentMode.GrossFirst));
            await context.SaveChangesAsync();
        }

        // Normal operation continues afterwards.
        var created = await harness.WeighmentService.CreateAsync(Request());
        Assert.Matches(@"^\d{6,}$", created.SlipNumber);
    }

    [Fact]
    public async Task DuplicateRealSlipNumbers_AreStillRefused()
    {
        using var harness = new MasterHarness();
        var existing = await harness.WeighmentService.CreateAsync(Request());

        await using var context = harness.CreateContext();
        var clash = Weighment.Open("MH12AA0020", WeighmentMode.GrossFirst);
        context.Add(clash);
        context.Entry(clash).Property<string>(nameof(Weighment.SlipNumber)).CurrentValue = existing.SlipNumber;

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>
    /// The partial unique index is the enforcement behind the "already has an open
    /// weighment" message: two operators racing past the service-level check cannot both
    /// open the same vehicle.
    /// </summary>
    [Fact]
    public async Task ASecondOpenWeighment_ForTheSameVehicle_IsRefusedByTheDatabase()
    {
        using var harness = new MasterHarness();
        await harness.WeighmentService.CreateAsync(Request());

        await using var context = harness.CreateContext();
        context.Add(Weighment.Open("MH12AB9999", WeighmentMode.TareFirst));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task CompletedAndCancelledRecords_DoNotCountAsOpen()
    {
        using var harness = new MasterHarness();
        var first = await harness.WeighmentService.CreateAsync(Request());
        await harness.WeighmentService.RecordFirstWeightAsync(first.Id, 20_000m, WeightSource.Indicator);
        var completed = await harness.WeighmentService.RecordSecondWeightAsync(first.Id, 8_000m, WeightSource.Manual);
        Assert.Equal(WeighmentStatus.Completed, completed.Status);

        // Same vehicle may be weighed again once the first transaction closed.
        var second = await harness.WeighmentService.CreateAsync(Request());
        Assert.NotEqual(second.Id, first.Id);
    }

    /// <summary>A soft-deleted master must not stay flagged active: the two flags disagree.</summary>
    [Fact]
    public async Task SoftDeletingAnActiveMaster_AlsoDeactivatesIt()
    {
        using var root = new TempDataRoot();
        var options = MigratedOptions(root);

        long materialId;
        await using (var seed = new WeighBridgeDbContext(options))
        {
            var material = Material.Create("Coal");
            seed.Set<Material>().Add(material);
            await seed.SaveChangesAsync();
            materialId = material.Id;
        }

        var signedIn = new WeighBridge.Core.Security.SignedInOperator { UserName = "auditor" };
        await using (var context = new WeighBridgeDbContext(options))
        {
            var repository = new EfRepository<Material>(context, signedIn);
            var material = await repository.GetByIdAsync(materialId);
            Assert.NotNull(material);
            repository.Remove(material!);
            await context.SaveChangesAsync();
        }

        await using (var verify = new WeighBridgeDbContext(options))
        {
            var row = await verify.Set<Material>().IgnoreQueryFilters().SingleAsync(m => m.Id == materialId);
            Assert.True(row.IsDeleted);
            Assert.False(row.IsActive);
            Assert.Equal("auditor", row.DeletedBy);
        }
    }

    /// <summary>The repository attributes writes to the signed-in operator.</summary>
    [Fact]
    public async Task RepositoryWrites_StampTheSignedInOperator()
    {
        using var root = new TempDataRoot();
        var options = MigratedOptions(root);

        var signedIn = new WeighBridge.Core.Security.SignedInOperator { UserName = "shifter" };

        long id;
        await using (var context = new WeighBridgeDbContext(options))
        {
            var repository = new EfRepository<Party>(context, signedIn);
            var party = Party.Create("Acme Corp");
            await repository.AddAsync(party);
            await context.SaveChangesAsync();
            id = party.Id;
        }

        await using (var edit = new WeighBridgeDbContext(options))
        {
            var repository = new EfRepository<Party>(edit, signedIn);
            var party = await repository.GetByIdAsync(id);
            Assert.NotNull(party);

            party!.Update("Acme Corp", code: "AC", address: null, contactNumber: null, email: null, remarks: null);
            repository.Update(party);
            await edit.SaveChangesAsync();
        }

        await using (var verify = new WeighBridgeDbContext(options))
        {
            var row = await verify.Set<Party>().SingleAsync(p => p.Id == id);
            Assert.Equal("shifter", row.CreatedBy);
            Assert.NotNull(row.ModifiedAtUtc);
            Assert.Equal("shifter", row.ModifiedBy);
        }
    }

    [Fact]
    public async Task AuditEntries_PersistWithOperatorAndOutcome()
    {
        using var root = new TempDataRoot();
        var options = MigratedOptions(root);

        var signedIn = new WeighBridge.Core.Security.SignedInOperator { UserName = "bridge_admin" };
        var store = new WeighBridge.Infrastructure.Persistence.Auditing.DatabaseAuditStore(
            () => new UnitOfWork(new WeighBridgeDbContext(options), signedIn),
            NullLogger<WeighBridge.Infrastructure.Persistence.Auditing.DatabaseAuditStore>.Instance);

        var before = DateTime.UtcNow.AddSeconds(-1);
        await store.WriteAsync(new WeighBridge.Core.Logging.AuditRecord(
            DateTime.UtcNow,
            signedIn.UserName,
            "Masters",
            "Created",
            "Succeeded",
            "Vehicle",
            "MH12AB7777",
            null,
            "corr123"));

        await store.WriteAsync(new WeighBridge.Core.Logging.AuditRecord(
            DateTime.UtcNow,
            signedIn.UserName,
            "Security",
            "SignedIn",
            "Failed",
            "User",
            "intruder",
            "Incorrect password.",
            "corr124"));

        await using var verify = new WeighBridgeDbContext(options);
        var entries = await verify.Set<WeighBridge.Infrastructure.Persistence.Auditing.AuditEntry>()
            .OrderBy(e => e.Id)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.All(entries, e => Assert.InRange(e.OccurredAtUtc, before, DateTime.UtcNow.AddSeconds(1)));
        Assert.Equal("Created", entries[0].Action);
        Assert.Equal("Succeeded", entries[0].Outcome);
        Assert.Equal("bridge_admin", entries[0].OperatorName);
        Assert.Equal("Masters", entries[0].Module);
        Assert.Equal("corr123", entries[0].CorrelationId);
        Assert.Equal("Failed", entries[1].Outcome);
        Assert.Contains("password", entries[1].Details);
        Assert.DoesNotContain("Bridge-Weigh", entries[1].Details ?? string.Empty);
    }

    private static DbContextOptions<WeighBridgeDbContext> MigratedOptions(TempDataRoot root)
    {
        root.Paths.EnsureCreated();

        var options = new DbContextOptionsBuilder<WeighBridgeDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root.Paths.DatabaseDirectory, "weighbridge.db")}")
            .Options;

        using var context = new WeighBridgeDbContext(options);
        context.Database.Migrate();
        return options;
    }
}


