using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Infrastructure.Repositories;

namespace WeighBridge.Tests.Weighments;

public class WeighmentServiceTests
{
    [Fact]
    public async Task FindPendingSecondEntryAsync_Returns_Only_AwaitingSecondWeight()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 15000m, WeightSource.Indicator);

        var found = await harness.Service.FindPendingSecondEntryAsync(open.SlipNumber);

        Assert.NotNull(found);
        Assert.Equal(open.Id, found.Id);
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, found.Status);
    }

    [Fact]
    public async Task FindPendingSecondEntryAsync_Excludes_Created()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });

        var bySlip = await harness.Service.FindPendingSecondEntryAsync(open.SlipNumber);
        var byVehicle = await harness.Service.FindPendingSecondEntryAsync("MH12AB1234");

        Assert.Null(bySlip);
        Assert.Null(byVehicle);
    }

    [Fact]
    public async Task FindPendingSecondEntryAsync_Excludes_Completed()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 15000m, WeightSource.Indicator);
        await harness.Service.RecordSecondWeightAsync(open.Id, 5000m, WeightSource.Indicator);

        var bySlip = await harness.Service.FindPendingSecondEntryAsync(open.SlipNumber);
        var byVehicle = await harness.Service.FindPendingSecondEntryAsync("MH12AB1234");

        Assert.Null(bySlip);
        Assert.Null(byVehicle);
    }

    [Fact]
    public async Task FindPendingSecondEntryAsync_Excludes_Cancelled()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });
        await harness.Service.CancelAsync(open.Id, "Mistyped entry");

        var bySlip = await harness.Service.FindPendingSecondEntryAsync(open.SlipNumber);
        var byVehicle = await harness.Service.FindPendingSecondEntryAsync("MH12AB1234");

        Assert.Null(bySlip);
        Assert.Null(byVehicle);
    }

    [Theory]
    [InlineData("WB-000001")]
    [InlineData("wb-000001")]
    [InlineData("1")]
    [InlineData("000001")]
    [InlineData("wb-1")]
    public async Task FindPendingSecondEntryAsync_Resolves_Canonical_And_Human_SlipNumbers(string searchKey)
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 15000m, WeightSource.Indicator);

        var found = await harness.Service.FindPendingSecondEntryAsync(searchKey);

        Assert.NotNull(found);
        Assert.Equal(open.Id, found.Id);
        Assert.Equal("WB-000001", found.SlipNumber);
    }

    [Theory]
    [InlineData("MH12AB1234")]
    [InlineData("mh12ab1234")]
    [InlineData("mh 12 ab 1234")]
    [InlineData("MH-12-AB-1234")]
    [InlineData("  mh-12.ab.1234  ")]
    public async Task FindPendingSecondEntryAsync_Resolves_VehicleRegistration(string searchKey)
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 15000m, WeightSource.Indicator);

        var found = await harness.Service.FindPendingSecondEntryAsync(searchKey);

        Assert.NotNull(found);
        Assert.Equal(open.Id, found.Id);
        Assert.Equal("MH12AB1234", found.VehicleNumber);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task FindPendingSecondEntryAsync_EmptySearchKey_Returns_Null(string? searchKey)
    {
        using var harness = new WeighmentHarness();
        var found = await harness.Service.FindPendingSecondEntryAsync(searchKey!);
        Assert.Null(found);
    }

    [Fact]
    public async Task FindPendingSecondEntryAsync_MultipleVehicleMatches_Throws_DeterministicException_NeverSelectsImplicitly()
    {
        using var harness = new WeighmentHarness();

        // Seed two distinct rows for the same vehicle in AwaitingSecondWeight via direct context to simulate legacy multi-ticket state.
        // We drop the unique filtered index for this specific test database so multiple open rows can coexist.
        await using (var db = harness.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_Weighments_VehicleNumber_Open;");

            var w1 = Weighment.Open("MH12AB9999", WeighmentMode.GrossFirst);
            w1.RecordFirstWeight(new WeightCapture(20000m, DateTime.UtcNow, WeightSource.Indicator));
            await db.Set<Weighment>().AddAsync(w1);
            await db.SaveChangesAsync();
            w1.AssignSlipNumber();

            var w2 = Weighment.Open("MH12AB9999", WeighmentMode.GrossFirst);
            w2.RecordFirstWeight(new WeightCapture(22000m, DateTime.UtcNow, WeightSource.Indicator));
            await db.Set<Weighment>().AddAsync(w2);
            await db.SaveChangesAsync();
            w2.AssignSlipNumber();

            await db.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service.FindPendingSecondEntryAsync("MH12AB9999"));

        Assert.Contains("Multiple pending transactions found", ex.Message);
        Assert.Contains("MH12AB9999", ex.Message);
    }

    [Fact]
    public async Task FindPendingSecondEntryAsync_Returns_Exact_Immutable_Historical_Snapshot()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12AB1234",
            PartyName = "Historical Party Ltd",
            MaterialName = "Iron Ore",
            DriverName = "John Doe",
            TransporterName = "Fast Logistics",
            Remarks = "First entry note",
            Charges = 150m,
            NumberOfBags = 20,
            BagWeightKg = 0.5m,
            GatePassNumber = "GP-1001",
            CustomField1 = "Container-A",
            CustomField2 = "Seal-999"
        });
        await harness.Service.RecordFirstWeightAsync(open.Id, 25000m, WeightSource.Indicator);

        var found = await harness.Service.FindPendingSecondEntryAsync(open.SlipNumber);

        Assert.NotNull(found);
        Assert.Equal("Historical Party Ltd", found.PartyName);
        Assert.Equal("Iron Ore", found.MaterialName);
        Assert.Equal("John Doe", found.DriverName);
        Assert.Equal("Fast Logistics", found.TransporterName);
        Assert.Equal("First entry note", found.Remarks);
        Assert.Equal(150m, found.Charges);
        Assert.Equal(20, found.NumberOfBags);
        Assert.Equal(0.5m, found.BagWeightKg);
        Assert.Equal("GP-1001", found.GatePassNumber);
        Assert.Equal("Container-A", found.CustomField1);
        Assert.Equal("Seal-999", found.CustomField2);
    }

    [Fact]
    public async Task UpdateSecondEntryDetails_Updates_Only_Allowed_F2_Fields()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12AB1234",
            PartyName = "Original Party",
            Charges = 100m,
            CustomField1 = "CF1-Original"
        });
        await harness.Service.RecordFirstWeightAsync(open.Id, 20000m, WeightSource.Indicator);

        var updated = await harness.Service.UpdateSecondEntryDetailsAsync(new UpdateSecondEntryDetailsRequest(
            WeighmentId: open.Id,
            SecondCharges: 50m,
            NumberOfBags: 10,
            BagWeightKg: 1.2m,
            GatePassNumber: "GP-2002",
            Remarks: "Updated in F2",
            CustomField3: "CF3-Value",
            CustomField4: "CF4-Value"
        ));

        Assert.Equal(50m, updated.SecondCharges);
        Assert.Equal(10, updated.NumberOfBags);
        Assert.Equal(1.2m, updated.BagWeightKg);
        Assert.Equal(12m, updated.TotalBagWeightKg);
        Assert.Equal("GP-2002", updated.GatePassNumber);
        Assert.Equal("Updated in F2", updated.Remarks);
        Assert.Equal("CF3-Value", updated.CustomField3);
        Assert.Equal("CF4-Value", updated.CustomField4);
    }

    [Fact]
    public async Task UpdateSecondEntryDetails_CannotChange_F1_Fields()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12AB1234",
            PartyName = "Original Party",
            MaterialName = "Coal",
            Charges = 100m,
            CustomField1 = "CF1-Orig",
            CustomField2 = "CF2-Orig"
        });
        await harness.Service.RecordFirstWeightAsync(open.Id, 20000m, WeightSource.Indicator);

        await harness.Service.UpdateSecondEntryDetailsAsync(new UpdateSecondEntryDetailsRequest(
            WeighmentId: open.Id,
            SecondCharges: 80m,
            NumberOfBags: 5,
            BagWeightKg: 2m,
            GatePassNumber: "GP-F2",
            Remarks: "F2 Remark"
        ));

        var reloaded = await harness.Service.GetAsync(open.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Original Party", reloaded.PartyName);
        Assert.Equal("Coal", reloaded.MaterialName);
        Assert.Equal(100m, reloaded.Charges);
        Assert.Equal("CF1-Orig", reloaded.CustomField1);
        Assert.Equal("CF2-Orig", reloaded.CustomField2);
    }

    [Fact]
    public async Task UpdateSecondEntryDetails_Rejects_Created_Weighment()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.UpdateSecondEntryDetailsAsync(new UpdateSecondEntryDetailsRequest(
                WeighmentId: open.Id,
                SecondCharges: 50m,
                NumberOfBags: null,
                BagWeightKg: null,
                GatePassNumber: null,
                Remarks: null
            )));

        Assert.Contains("Second-entry details can only be updated while awaiting second weight", ex.Message);
    }

    [Fact]
    public async Task UpdateSecondEntryDetails_Rejects_Completed_Weighment()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 20000m, WeightSource.Indicator);
        await harness.Service.RecordSecondWeightAsync(open.Id, 8000m, WeightSource.Indicator);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.UpdateSecondEntryDetailsAsync(new UpdateSecondEntryDetailsRequest(
                WeighmentId: open.Id,
                SecondCharges: 50m,
                NumberOfBags: null,
                BagWeightKg: null,
                GatePassNumber: null,
                Remarks: null
            )));

        Assert.Contains("Second-entry details can only be updated while awaiting second weight", ex.Message);
    }

    [Fact]
    public async Task UpdateSecondEntryDetails_Rejects_Cancelled_Weighment()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });
        await harness.Service.CancelAsync(open.Id, "Voided");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.UpdateSecondEntryDetailsAsync(new UpdateSecondEntryDetailsRequest(
                WeighmentId: open.Id,
                SecondCharges: 50m,
                NumberOfBags: null,
                BagWeightKg: null,
                GatePassNumber: null,
                Remarks: null
            )));

        Assert.Contains("Second-entry details can only be updated while awaiting second weight", ex.Message);
    }

    [Fact]
    public async Task UpdateSecondEntryDetails_Rejects_Unauthorized_Role()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 20000m, WeightSource.Indicator);

        // Operator role lacks WeighmentEdit permission
        harness.SignInAs(Roles.Operator);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.Service.UpdateSecondEntryDetailsAsync(new UpdateSecondEntryDetailsRequest(
                WeighmentId: open.Id,
                SecondCharges: 50m,
                NumberOfBags: null,
                BagWeightKg: null,
                GatePassNumber: null,
                Remarks: null
            )));

        // ReadOnly role lacks WeighmentEdit permission
        harness.SignInAs(Roles.ReadOnly);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.Service.UpdateSecondEntryDetailsAsync(new UpdateSecondEntryDetailsRequest(
                WeighmentId: open.Id,
                SecondCharges: 50m,
                NumberOfBags: null,
                BagWeightKg: null,
                GatePassNumber: null,
                Remarks: null
            )));

        // Supervisor role possesses WeighmentEdit permission
        harness.SignInAs(Roles.Supervisor);
        var supervisorUpdated = await harness.Service.UpdateSecondEntryDetailsAsync(new UpdateSecondEntryDetailsRequest(
            WeighmentId: open.Id,
            SecondCharges: 50m,
            NumberOfBags: null,
            BagWeightKg: null,
            GatePassNumber: null,
            Remarks: "Supervisor updated"
        ));
        Assert.Equal(50m, supervisorUpdated.SecondCharges);

        // Administrator role possesses WeighmentEdit permission
        harness.SignInAs(Roles.Administrator);
        var adminUpdated = await harness.Service.UpdateSecondEntryDetailsAsync(new UpdateSecondEntryDetailsRequest(
            WeighmentId: open.Id,
            SecondCharges: 75m,
            NumberOfBags: null,
            BagWeightKg: null,
            GatePassNumber: null,
            Remarks: "Admin updated"
        ));
        Assert.Equal(75m, adminUpdated.SecondCharges);
    }

    [Fact]
    public async Task RecordSecondWeight_Rejects_Unauthorized_Role()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 20000m, WeightSource.Indicator);

        // Operator role lacks WeighmentEdit permission
        harness.SignInAs(Roles.Operator);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.Service.RecordSecondWeightAsync(open.Id, 8000m, WeightSource.Indicator));

        // ReadOnly role lacks WeighmentEdit permission
        harness.SignInAs(Roles.ReadOnly);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            harness.Service.RecordSecondWeightAsync(open.Id, 8000m, WeightSource.Indicator));

        // Supervisor role possesses WeighmentEdit permission
        harness.SignInAs(Roles.Supervisor);
        var completed = await harness.Service.RecordSecondWeightAsync(open.Id, 8000m, WeightSource.Indicator);
        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(12000m, completed.NetWeightKg);
    }

    [Fact]
    public async Task UpdateSecondEntryDetails_Converts_ConcurrencyConflict_Safely()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12AB1234" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 20000m, WeightSource.Indicator);

        // Simulate a concurrency conflict at the UoW / change tracker boundary
        await using var uow = new UnitOfWork(harness.CreateContext(), harness.Operator);
        var repo = uow.Repository<Weighment>();
        var entity = await repo.GetByIdAsync(open.Id);
        Assert.NotNull(entity);

        // Parallel context changes version in DB
        await using (var concurrentContext = harness.CreateContext())
        {
            var record = await concurrentContext.Set<Weighment>().FindAsync(open.Id);
            Assert.NotNull(record);
            record.UpdateSecondEntryDetails(20m, null, null, null, "Concurrent edit");
            await concurrentContext.SaveChangesAsync();
        }

        // Stale entity update must throw converted friendly InvalidOperationException
        entity.UpdateSecondEntryDetails(90m, null, null, null, "Stale edit");
        repo.Update(entity);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            try
            {
                await uow.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException conEx)
            {
                throw new InvalidOperationException(
                    $"Weighment {entity.SlipNumber} was modified by another operator or process. Please reload the transaction before making changes.",
                    conEx);
            }
        });

        // The error message must be operator-friendly
        Assert.Contains("modified by another operator or process", ex.Message);
    }

    [Fact]
    public async Task RecordSecondWeight_Rejects_Negative_Net_In_Both_Modes()
    {
        using var harness = new WeighmentHarness();

        // Mode 1: GrossFirst (First weight = 10,000 kg). Second weight (Tare) = 15,000 kg -> Net would be -5,000 kg
        var grossFirst = await harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12GF0001",
            Mode = WeighmentMode.GrossFirst
        });
        await harness.Service.RecordFirstWeightAsync(grossFirst.Id, 10000m, WeightSource.Indicator);

        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.RecordSecondWeightAsync(grossFirst.Id, 15000m, WeightSource.Indicator));
        Assert.Contains("gross weight", ex1.Message);
        Assert.Contains("greater than the tare weight", ex1.Message);

        // Mode 2: TareFirst (First weight = 18,000 kg). Second weight (Gross) = 12,000 kg -> Net would be -6,000 kg
        var tareFirst = await harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12TF0002",
            Mode = WeighmentMode.TareFirst
        });
        await harness.Service.RecordFirstWeightAsync(tareFirst.Id, 18000m, WeightSource.Indicator);

        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.RecordSecondWeightAsync(tareFirst.Id, 12000m, WeightSource.Indicator));
        Assert.Contains("gross weight", ex2.Message);
        Assert.Contains("greater than the tare weight", ex2.Message);
    }

    [Fact]
    public async Task RecordSecondWeight_ZeroNet_Respects_AllowZeroNetWeight_Setting()
    {
        // 1. Default configuration: AllowZeroNetWeight = false
        using (var harnessDefault = new WeighmentHarness())
        {
            var open = await harnessDefault.Service.CreateAsync(new NewWeighment
            {
                VehicleNumber = "MH12ZN0001",
                Mode = WeighmentMode.GrossFirst
            });
            await harnessDefault.Service.RecordFirstWeightAsync(open.Id, 10000m, WeightSource.Indicator);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                harnessDefault.Service.RecordSecondWeightAsync(open.Id, 10000m, WeightSource.Indicator));

            Assert.Contains("Zero net weight is disallowed by policy", ex.Message);
        }

        // 2. Permissive configuration: AllowZeroNetWeight = true
        using (var harnessAllowZero = new WeighmentHarness(Options.Create(new WeighmentOptions { AllowZeroNetWeight = true })))
        {
            var open = await harnessAllowZero.Service.CreateAsync(new NewWeighment
            {
                VehicleNumber = "MH12ZN0002",
                Mode = WeighmentMode.GrossFirst
            });
            await harnessAllowZero.Service.RecordFirstWeightAsync(open.Id, 10000m, WeightSource.Indicator);

            var completed = await harnessAllowZero.Service.RecordSecondWeightAsync(open.Id, 10000m, WeightSource.Indicator);

            Assert.Equal(WeighmentStatus.Completed, completed.Status);
            Assert.Equal(0m, completed.NetWeightKg);
        }
    }

    [Fact]
    public async Task RecordSecondWeight_BagDeductions_Exceeding_Net_Throws()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12BAG001",
            Mode = WeighmentMode.GrossFirst
        });
        await harness.Service.RecordFirstWeightAsync(open.Id, 20000m, WeightSource.Indicator);

        // Net weight will be 20,000 - 19,000 = 1,000 kg
        // Bag deduction: 200 bags * 10 kg/bag = 2,000 kg deduction > 1,000 kg net
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.RecordSecondWeightAsync(new RecordSecondWeightRequest(
                WeighmentId: open.Id,
                Kilograms: 19000m,
                Source: WeightSource.Indicator,
                NumberOfBags: 200,
                BagWeightKg: 10m
            )));

        Assert.Contains("exceeds the net weight", ex.Message);
        Assert.Contains("negative actual material weight", ex.Message);
    }

    [Fact]
    public async Task ConcurrentSameVehicleF1_100Attempts_Distinguishes_BusinessConflict_From_TransientLock()
    {
        using var harness = new WeighmentHarness();
        const string vehicleNumber = "MH12RACE01";
        const int totalAttempts = 100;

        var tasks = Enumerable.Range(1, totalAttempts).Select(async _ =>
        {
            try
            {
                var w = await harness.Service.CreateAsync(new NewWeighment
                {
                    VehicleNumber = vehicleNumber,
                    Mode = WeighmentMode.GrossFirst
                });
                return (Success: true, Message: "Success");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already has an open weighment"))
            {
                return (Success: false, Message: "BusinessConflict");
            }
            catch (Exception ex)
            {
                return (Success: false, Message: $"UnexpectedError: {ex.GetType().Name} - {ex.Message}");
            }
        });

        var results = await Task.WhenAll(tasks);

        var successCount = results.Count(r => r.Success);
        var conflictCount = results.Count(r => r.Message == "BusinessConflict");
        var otherErrors = results.Where(r => !r.Success && r.Message != "BusinessConflict").ToList();

        Assert.Equal(1, successCount);
        Assert.Equal(totalAttempts - 1, conflictCount);
        Assert.Empty(otherErrors);
    }

    [Fact]
    public async Task ConcurrentDifferentVehicles_100Attempts_AllSucceed()
    {
        using var harness = new WeighmentHarness();
        const int totalAttempts = 100;

        var tasks = Enumerable.Range(1, totalAttempts).Select(async i =>
        {
            var vehicle = $"MH12DIFF{i:D4}";
            return await harness.Service.CreateAsync(new NewWeighment
            {
                VehicleNumber = vehicle,
                Mode = WeighmentMode.GrossFirst
            });
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(totalAttempts, results.Length);
        var distinctSlips = results.Select(r => r.SlipNumber).Distinct().Count();
        Assert.Equal(totalAttempts, distinctSlips);
    }

    [Fact]
    public async Task StaleF2Update_DoesNotOverwrite_NewerTransaction()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12STALE1" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 25000m, WeightSource.Indicator);

        // Context A loads the entity
        await using var contextA = harness.CreateContext();
        var entityA = await contextA.Set<Weighment>().FindAsync(open.Id);
        Assert.NotNull(entityA);

        // Context B loads the same entity, mutates and commits
        await using (var contextB = harness.CreateContext())
        {
            var entityB = await contextB.Set<Weighment>().FindAsync(open.Id);
            Assert.NotNull(entityB);
            entityB.UpdateSecondEntryDetails(100m, 10, 1m, "GP-B", "Saved by Context B");
            await contextB.SaveChangesAsync();
        }

        // Context A now attempts to save its stale entity
        entityA.UpdateSecondEntryDetails(500m, 50, 2m, "GP-A", "Saved by Context A");

        // EF Core must throw DbUpdateConcurrencyException due to Version mismatch
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextA.SaveChangesAsync());

        // Verify the database still contains Context B's committed state
        var finalReload = await harness.Service.GetAsync(open.Id);
        Assert.NotNull(finalReload);
        Assert.Equal(100m, finalReload.SecondCharges);
        Assert.Equal("GP-B", finalReload.GatePassNumber);
        Assert.Equal("Saved by Context B", finalReload.Remarks);
    }

    [Fact]
    public async Task ConcurrencyConflict_Emits_No_Success_Audit_Or_Events()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12EVT001" });
        await harness.Service.RecordFirstWeightAsync(open.Id, 20000m, WeightSource.Indicator);

        // Track published events
        var completedEvents = new List<WeighmentCompletedEvent>();
        using var subscription = harness.Events.Subscribe<WeighmentCompletedEvent>(completedEvents.Add);

        // Load stale entity in UoW
        await using var uow = new UnitOfWork(harness.CreateContext(), harness.Operator);
        var repo = uow.Repository<Weighment>();
        var entity = await repo.GetByIdAsync(open.Id);
        Assert.NotNull(entity);

        // Mutate in parallel context to create Version conflict
        await using (var db = harness.CreateContext())
        {
            var rec = await db.Set<Weighment>().FindAsync(open.Id);
            Assert.NotNull(rec);
            rec.UpdateSecondEntryDetails(50m, null, null, null, "Context update");
            await db.SaveChangesAsync();
        }

        // Attempting second weight on stale transaction via UoW
        entity.RecordSecondWeight(new WeightCapture(8000m, DateTime.UtcNow, WeightSource.Indicator));
        repo.Update(entity);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => uow.SaveChangesAsync());

        // Verifying 0 completion events were emitted
        Assert.Empty(completedEvents);
    }

    [Fact]
    public async Task RecordSecondWeight_InvalidInput_DoesNotPersistF2Details()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12ATOMIC1",
            Charges = 100m
        });
        await harness.Service.RecordFirstWeightAsync(open.Id, 10000m, WeightSource.Indicator);

        // Attempt second weight with invalid second weight (GrossFirst: 10,000 kg first, 15,000 kg second -> invalid negative net)
        // while also supplying F2 details
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.Service.RecordSecondWeightAsync(new RecordSecondWeightRequest(
                WeighmentId: open.Id,
                Kilograms: 15000m, // Invalid for GrossFirst
                Source: WeightSource.Indicator,
                SecondCharges: 250m,
                NumberOfBags: 50,
                BagWeightKg: 1m,
                GatePassNumber: "GP-ATOMIC-FAIL",
                Remarks: "Should not persist"
            )));

        Assert.Contains("gross weight", ex.Message);

        // Reload from DB and verify atomic rollback: status remains AwaitingSecondWeight, F2 fields not saved
        var reloaded = await harness.Service.GetAsync(open.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, reloaded.Status);
        Assert.Equal(0m, reloaded.SecondCharges);
        Assert.Null(reloaded.NumberOfBags);
        Assert.Null(reloaded.BagWeightKg);
        Assert.Null(reloaded.GatePassNumber);
        Assert.Null(reloaded.Remarks);
        Assert.Null(reloaded.NetWeightKg);
        Assert.Null(reloaded.CompletedAtUtc);
    }

    [Fact]
    public async Task RecordSecondWeight_SimpleOverload_UsesSameBusinessRules()
    {
        using var harness = new WeighmentHarness();
        var open = await harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12SIMPLE1",
            Mode = WeighmentMode.GrossFirst
        });
        await harness.Service.RecordFirstWeightAsync(open.Id, 25000m, WeightSource.Indicator);

        // Track events
        var completedEvents = new List<WeighmentCompletedEvent>();
        using var sub = harness.Events.Subscribe<WeighmentCompletedEvent>(completedEvents.Add);

        // Execute simple overload
        var completed = await harness.Service.RecordSecondWeightAsync(open.Id, 10000m, WeightSource.Indicator);

        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(15000m, completed.NetWeightKg);
        Assert.Single(completedEvents);
        Assert.Equal(open.SlipNumber, completedEvents[0].SlipNumber);
        Assert.Equal(15000m, completedEvents[0].NetKilograms);
    }
}
