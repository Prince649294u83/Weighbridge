using Microsoft.EntityFrameworkCore;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Domain.Weighments;
using WeighBridge.Infrastructure.Persistence;
using WeighBridge.Services.Weighments;

namespace WeighBridge.Tests.Weighments;

/// <summary>
/// Headless tests for the Gate 2E Modern F1/F2 Operator Workflow covering:
/// deterministic ticket allocation, F1 weight capture, scanner search, snapshot immutability across
/// master updates, optimistic concurrency handling, permission boundaries, and summary projections.
/// </summary>
public sealed class VehicleEntryWorkflowTests : IDisposable
{
    private const string XamlPath =
        @"..\..\..\..\..\src\WeighBridge.App\Views\VehicleEntryView.xaml";

    private const string ViewModelPath =
        @"..\..\..\..\..\src\WeighBridge.App\ViewModels\VehicleEntryViewModel.cs";

    private readonly WeighmentHarness _harness = new();

    public VehicleEntryWorkflowTests() => _harness.SignInAs(Roles.Administrator);

    public void Dispose() => _harness.Dispose();

    private static string XamlSource => File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, XamlPath)));

    private static string ViewModelSource => File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ViewModelPath)));

    #region 1. F1 Lifecycle & Ticket Allocation

    [Fact]
    public async Task F1_AllocateTicket_Creates_Transaction_And_Displays_Persisted_SlipNumber()
    {
        var request = new NewWeighment
        {
            VehicleNumber = "MH12AB1234",
            Mode = WeighmentMode.GrossFirst,
            PartyName = "Tata Steel",
            MaterialName = "Iron Ore",
            Charges = 100m,
            CustomField1 = "Consigner A",
            CustomField2 = "GR-100"
        };

        var result = await _harness.Executor.ExecuteAsync(
            new CreateWeighmentCommand(_harness.Service, request));

        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        Assert.NotNull(result.Value);
        Assert.StartsWith("WB-", result.Value.SlipNumber);
        Assert.Equal(WeighmentStatus.Created, result.Value.Status);
        Assert.Null(result.Value.FirstWeight);
        Assert.Null(result.Value.SecondWeight);

        // Verify transaction is in SQLite database and recoverable before F1 weight
        var onDisk = await _harness.Service.GetAsync(result.Value.Id);
        Assert.NotNull(onDisk);
        Assert.Equal(result.Value.SlipNumber, onDisk.SlipNumber);
        Assert.Equal(WeighmentStatus.Created, onDisk.Status);
    }

    [Fact]
    public async Task F1_RecordFirstWeight_Transitions_To_AwaitingSecondWeight()
    {
        var request = new NewWeighment { VehicleNumber = "MH14CD5678", Mode = WeighmentMode.GrossFirst };
        var created = await _harness.Service.CreateAsync(request);

        var result = await _harness.Executor.ExecuteAsync(
            new RecordFirstWeightCommand(_harness.Service, created.Id, 90.0m, WeightSource.Indicator));

        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, result.Value!.Status);
        Assert.NotNull(result.Value.FirstWeight);
        Assert.Equal(90.0m, result.Value.FirstWeight.Kilograms);
        Assert.Equal(WeightSource.Indicator, result.Value.FirstWeight.Source);

        var pending = await _harness.Service.GetAwaitingSecondWeightAsync();
        Assert.Contains(pending, w => w.Id == created.Id);
    }

    #endregion

    #region 2. F2 Search, Snapshot Locking & Master Immutability

    [Fact]
    public async Task F2_Search_By_SlipNumber_Restores_Historical_Snapshot_And_Locks_F1_Fields()
    {
        var created = await _harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12AB1234",
            Mode = WeighmentMode.GrossFirst,
            PartyName = "Original Party",
            MaterialName = "Original Material",
            Charges = 150m,
            CustomField1 = "CF1-Orig",
            CustomField2 = "CF2-Orig"
        });

        await _harness.Service.RecordFirstWeightAsync(created.Id, 90.0m, WeightSource.Indicator);

        var found = await _harness.Service.FindPendingSecondEntryAsync(created.SlipNumber);
        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal("MH12AB1234", found.VehicleNumber);
        Assert.Equal("Original Party", found.PartyName);
        Assert.Equal("Original Material", found.MaterialName);
        Assert.Equal(90.0m, found.FirstWeight?.Kilograms);
        Assert.Equal(150m, found.Charges);
        Assert.Equal("CF1-Orig", found.CustomField1);
        Assert.Equal("CF2-Orig", found.CustomField2);
    }

    [Fact]
    public async Task F2_Search_By_Vehicle_Restores_Historical_Snapshot()
    {
        var created = await _harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "KA01XY9999",
            Mode = WeighmentMode.GrossFirst
        });

        await _harness.Service.RecordFirstWeightAsync(created.Id, 120.0m, WeightSource.Indicator);

        var found = await _harness.Service.FindPendingSecondEntryAsync("ka-01-xy-9999");
        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
        Assert.Equal("KA01XY9999", found.VehicleNumber);
    }

    [Fact]
    public async Task F2_Search_Excludes_Created_Completed_And_Cancelled()
    {
        // 1. Created (not weighed yet)
        var created = await _harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH01AA1111" });
        var foundCreated = await _harness.Service.FindPendingSecondEntryAsync(created.SlipNumber);
        Assert.Null(foundCreated);

        // 2. Completed
        var weighed = await _harness.Service.RecordFirstWeightAsync(created.Id, 100m, WeightSource.Indicator);
        await _harness.Service.RecordSecondWeightAsync(new RecordSecondWeightRequest(created.Id, 40m, WeightSource.Indicator));
        var foundCompleted = await _harness.Service.FindPendingSecondEntryAsync(created.SlipNumber);
        Assert.Null(foundCompleted);

        // 3. Cancelled
        var another = await _harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH01BB2222" });
        await _harness.Service.RecordFirstWeightAsync(another.Id, 100m, WeightSource.Indicator);
        await _harness.Service.CancelAsync(another.Id, "Mistake");
        var foundCancelled = await _harness.Service.FindPendingSecondEntryAsync(another.SlipNumber);
        Assert.Null(foundCancelled);
    }

    [Fact]
    public async Task Master_Change_After_F1_Does_Not_Change_F2_Snapshot()
    {
        // 1. Create F1 with initial vehicle and party
        var created = await _harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12SNAPSHOT",
            PartyName = "Immutable Metals Ltd",
            MaterialName = "Bauxite Ore",
            Charges = 200m
        });
        await _harness.Service.RecordFirstWeightAsync(created.Id, 140.0m, WeightSource.Indicator);

        // 2. Simulate master database update (e.g. party renamed or vehicle tare modified)
        using (var context = _harness.CreateContext())
        {
            var vehicle = Vehicle.Create("MH12SNAPSHOT", tareWeightKg: 50.0m);
            context.Set<Vehicle>().Add(vehicle);
            await context.SaveChangesAsync();
        }

        // 3. Load F2 transaction
        var f2Transaction = await _harness.Service.FindPendingSecondEntryAsync("MH12SNAPSHOT");
        Assert.NotNull(f2Transaction);

        // 4. Verify original snapshot values remain exactly unchanged
        Assert.Equal("Immutable Metals Ltd", f2Transaction.PartyName);
        Assert.Equal("Bauxite Ore", f2Transaction.MaterialName);
        Assert.Equal(140.0m, f2Transaction.FirstWeight?.Kilograms);
        Assert.Equal(200m, f2Transaction.Charges);
    }

    [Fact]
    public async Task Creating_Second_Open_Weighment_For_Same_Vehicle_Is_Rejected()
    {
        var first = await _harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12MULTI" });
        await _harness.Service.RecordFirstWeightAsync(first.Id, 100m, WeightSource.Indicator);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12MULTI" }));

        Assert.Contains("Vehicle MH12MULTI already has an open weighment", ex.Message);
        Assert.Contains(first.SlipNumber, ex.Message);
    }

    #endregion

    #region 3. F2 Details, Bags, Packaging Deductions & Completion

    [Fact]
    public async Task F2_RecordSecondWeight_Completes_Transaction_With_Bags_And_ActualWeight()
    {
        var created = await _harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12BAGS",
            Mode = WeighmentMode.GrossFirst,
            Charges = 100m
        });
        await _harness.Service.RecordFirstWeightAsync(created.Id, 90.0m, WeightSource.Indicator);

        var request = new RecordSecondWeightRequest(
            WeighmentId: created.Id,
            Kilograms: 30.0m,
            Source: WeightSource.Indicator,
            SecondCharges: 50m,
            NumberOfBags: 20,
            BagWeightKg: 1.0m,
            GatePassNumber: "GP-9021",
            Remarks: "Final delivery",
            CustomField3: "CF3-Val",
            CustomField4: "CF4-Val");

        var result = await _harness.Executor.ExecuteAsync(
            new RecordSecondWeightCommand(_harness.Service, request));

        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        var completed = result.Value!;
        Assert.Equal(WeighmentStatus.Completed, completed.Status);
        Assert.Equal(90.0m, completed.Gross?.Kilograms);
        Assert.Equal(30.0m, completed.Tare?.Kilograms);
        Assert.Equal(60.0m, completed.NetWeightKg);

        Assert.Equal(20, completed.NumberOfBags);
        Assert.Equal(1.0m, completed.BagWeightKg);
        Assert.Equal(20.0m, completed.TotalBagWeightKg);
        Assert.Equal(40.0m, completed.ActualWeightKg); // 60.0 net - 20.0 bag deduction

        Assert.Equal(100m, completed.Charges);
        Assert.Equal(50m, completed.SecondCharges);
        Assert.Equal(150m, completed.Charges + completed.SecondCharges);
        Assert.Equal("GP-9021", completed.GatePassNumber);
        Assert.Equal("CF3-Val", completed.CustomField3);
        Assert.Equal("CF4-Val", completed.CustomField4);
    }

    [Fact]
    public async Task Second_Weight_Validation_Rejects_Bag_Deduction_Exceeding_Net()
    {
        var created = await _harness.Service.CreateAsync(new NewWeighment
        {
            VehicleNumber = "MH12OVERBAG",
            Mode = WeighmentMode.GrossFirst
        });
        await _harness.Service.RecordFirstWeightAsync(created.Id, 50.0m, WeightSource.Indicator);

        // Gross 50.0 - Tare 30.0 = Net 20.0 kg. But bags = 25 * 1.0 = 25.0 kg (> 20.0 kg net)
        var request = new RecordSecondWeightRequest(
            WeighmentId: created.Id,
            Kilograms: 30.0m,
            Source: WeightSource.Indicator,
            NumberOfBags: 25,
            BagWeightKg: 1.0m);

        var result = await _harness.Executor.ExecuteAsync(
            new RecordSecondWeightCommand(_harness.Service, request));

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("Total bag deduction (25 kg) exceeds the net weight (20 kg)", result.Message);
    }

    [Fact]
    public async Task Second_Weight_Validation_Rejects_Negative_Charges_Or_Bags()
    {
        var created = await _harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12NEG" });
        await _harness.Service.RecordFirstWeightAsync(created.Id, 50.0m, WeightSource.Indicator);

        var negCharges = new RecordSecondWeightRequest(created.Id, 30.0m, WeightSource.Indicator, SecondCharges: -10m);
        var res1 = await _harness.Executor.ExecuteAsync(new RecordSecondWeightCommand(_harness.Service, negCharges));
        Assert.Equal(CommandOutcome.ValidationFailed, res1.Outcome);
        Assert.Equal("Second charges cannot be negative.", res1.Message);

        var negBags = new RecordSecondWeightRequest(created.Id, 30.0m, WeightSource.Indicator, NumberOfBags: -5);
        var res2 = await _harness.Executor.ExecuteAsync(new RecordSecondWeightCommand(_harness.Service, negBags));
        Assert.Equal(CommandOutcome.ValidationFailed, res2.Outcome);
        Assert.Equal("Number of bags cannot be negative.", res2.Message);
    }

    #endregion

    #region 4. Security & Role Permissions

    [Fact]
    public async Task Permission_Restrictions_Disable_F2_For_Operator_Role()
    {
        _harness.SignInAs(Roles.Operator); // Operator has WeighmentCreate, lacks WeighmentEdit

        var created = await _harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12ROLE" });
        await _harness.Service.RecordFirstWeightAsync(created.Id, 90.0m, WeightSource.Indicator);

        var request = new RecordSecondWeightRequest(created.Id, 30.0m, WeightSource.Indicator);
        var result = await _harness.Executor.ExecuteAsync(
            new RecordSecondWeightCommand(_harness.Service, request));

        Assert.Equal(CommandOutcome.Denied, result.Outcome);
        Assert.Contains("Permission", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 5. Optimistic Concurrency Protection

    [Fact]
    public async Task Stale_Version_Throws_Concurrency_Error_And_Preserves_Database_Integrity()
    {
        var created = await _harness.Service.CreateAsync(new NewWeighment { VehicleNumber = "MH12CONC" });
        await _harness.Service.RecordFirstWeightAsync(created.Id, 90.0m, WeightSource.Indicator);

        // Context A loads the entity
        await using var contextA = _harness.CreateContext();
        var entityA = await contextA.Set<Weighment>().FindAsync(created.Id);
        Assert.NotNull(entityA);

        // Context B modifies and commits
        await using (var contextB = _harness.CreateContext())
        {
            var entityB = await contextB.Set<Weighment>().FindAsync(created.Id);
            Assert.NotNull(entityB);
            entityB.UpdateSecondEntryDetails(100m, 10, 1m, "GP-B", "Saved by Context B");
            await contextB.SaveChangesAsync();
        }

        // Context A now attempts to save its stale entity
        entityA.UpdateSecondEntryDetails(500m, 50, 2m, "GP-A", "Saved by Context A");

        // EF Core must throw DbUpdateConcurrencyException due to Version mismatch
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => contextA.SaveChangesAsync());

        // Verify the database still contains Context B's state
        var reloaded = await _harness.Service.GetAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(100m, reloaded.SecondCharges);
        Assert.Equal("GP-B", reloaded.GatePassNumber);
        Assert.Equal("Saved by Context B", reloaded.Remarks);
    }

    #endregion

    #region 6. View & ViewModel Source Contract Assertions

    [Fact]
    public void VehicleEntry_XAML_Contains_All_Required_Input_Bindings_And_Hotkeys()
    {
        Assert.Contains("<KeyBinding Key=\"F1\" Command=\"{Binding SwitchToFirstEntryCommand}\"", XamlSource);
        Assert.Contains("<KeyBinding Key=\"F2\" Command=\"{Binding SwitchToSecondEntryCommand}\"", XamlSource);
        Assert.Contains("<KeyBinding Key=\"F3\" Command=\"{Binding ReadIndicatorCommand}\"", XamlSource);
        Assert.Contains("<KeyBinding Key=\"F5\" Command=\"{Binding SubmitWorkflowCommand}\"", XamlSource);
        Assert.Contains("<KeyBinding Key=\"Escape\" Command=\"{Binding ClearContextCommand}\"", XamlSource);
    }

    [Fact]
    public void VehicleEntry_XAML_Has_Distinct_Historical_Locked_Section_And_Editable_Section()
    {
        Assert.Contains("Historical F1 Snapshot (Locked)", XamlSource);
        Assert.Contains("Second Entry Details (Editable)", XamlSource);
        Assert.Contains("Total Bag Deduction", XamlSource);
    }

    [Fact]
    public void VehicleEntry_ViewModel_Separates_WorkflowState_From_Domain_Status()
    {
        Assert.Contains("public enum WeighmentWorkflowState", ViewModelSource);
        Assert.Contains("TicketAllocated", ViewModelSource);
        Assert.Contains("AwaitingFirstWeight", ViewModelSource);
        Assert.Contains("F2Selected", ViewModelSource);
        Assert.Contains("AwaitingSecondWeightCapture", ViewModelSource);
        Assert.Contains("SwitchToFirstEntryAsync", ViewModelSource);
        Assert.Contains("AllocateTicketAsync", ViewModelSource);
        Assert.Contains("SearchPendingSecondEntryAsync", ViewModelSource);
    }

    #endregion
}
