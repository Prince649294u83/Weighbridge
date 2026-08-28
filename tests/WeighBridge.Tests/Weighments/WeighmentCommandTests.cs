using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Services.Weighments;

namespace WeighBridge.Tests.Weighments;

/// <summary>
/// Vehicle Entry through the command pipeline, over the real service and the real database.
/// </summary>
/// <remarks>
/// <para>
/// This is the layer the screen actually calls, so it is the layer that decides what the
/// operator sees. Two things are asserted throughout: the outcome, because the screen colours
/// its status line from it, and the message, because the notification centre has no visual
/// host yet and that message is the operator's only report of what happened.
/// </para>
/// <para>
/// The distinction between <see cref="CommandOutcome.ValidationFailed"/> and
/// <see cref="CommandOutcome.Failed"/> is the point of several of these tests. Everything an
/// operator can put right has to arrive as the former, with a sentence naming what to fix;
/// only a genuine fault should be the latter.
/// </para>
/// </remarks>
public sealed class WeighmentCommandTests : IDisposable
{
    private readonly WeighmentHarness _harness = new();

    public WeighmentCommandTests() => _harness.SignInAs(Roles.Administrator);

    public void Dispose() => _harness.Dispose();

    private Task<CommandResult<Weighment>> Open(
        string vehicleNumber = "MH12AB1234",
        WeighmentMode mode = WeighmentMode.GrossFirst)
        => _harness.Executor.ExecuteAsync(new CreateWeighmentCommand(
            _harness.Service,
            new NewWeighment { VehicleNumber = vehicleNumber, Mode = mode }));

    private Task<CommandResult<Weighment>> First(long id, decimal kilograms)
        => _harness.Executor.ExecuteAsync(
            new RecordFirstWeightCommand(_harness.Service, id, kilograms, WeightSource.Indicator));

    private Task<CommandResult<Weighment>> Second(long id, decimal kilograms)
        => _harness.Executor.ExecuteAsync(
            new RecordSecondWeightCommand(_harness.Service, id, kilograms, WeightSource.Manual));

    private Task<CommandResult> Cancel(long id, string reason)
        => _harness.Executor.ExecuteAsync(new CancelWeighmentCommand(_harness.Service, id, reason));

    /// <summary>
    /// The whole job, in the order an operator does it. Recording the second weight is what
    /// completes the weighment — there is no fourth step and no separate complete command.
    /// </summary>
    [Fact]
    public async Task TheWholeWorkflow_RunsThroughThePipeline()
    {
        var opened = await Open();

        Assert.Equal(CommandOutcome.Succeeded, opened.Outcome);
        Assert.Equal("WB-000001", opened.Value!.SlipNumber);
        Assert.Contains("WB-000001", opened.Message!, StringComparison.Ordinal);
        Assert.Contains("MH12AB1234", opened.Message!, StringComparison.Ordinal);

        var weighed = await First(opened.Value.Id, 32_500m);

        Assert.Equal(CommandOutcome.Succeeded, weighed.Outcome);
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, weighed.Value!.Status);
        Assert.Contains("32500 kg recorded on WB-000001", weighed.Message!, StringComparison.Ordinal);
        Assert.Contains("Record tare weight", weighed.Message!, StringComparison.Ordinal);

        var completed = await Second(opened.Value.Id, 12_250.5m);

        Assert.Equal(CommandOutcome.Succeeded, completed.Outcome);
        Assert.Equal(WeighmentStatus.Completed, completed.Value!.Status);
        Assert.Equal(20_249.5m, completed.Value.NetWeightKg);
        Assert.Equal("Weighment WB-000001 completed. Net 20249.5 kg.", completed.Message);

        // And it is on disk, not just in hand.
        var saved = await _harness.Service.GetBySlipNumberAsync("WB-000001");
        Assert.Equal(WeighmentStatus.Completed, saved!.Status);
        Assert.Equal(20_249.5m, saved.NetWeightKg);
    }

    [Fact]
    public async Task OpeningWithNothingTyped_FailsValidation_AndWritesNothing()
    {
        var result = await Open(vehicleNumber: "   ");

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.NotNull(result.Validation);
        Assert.Equal("Enter the vehicle number.", result.Message);
        Assert.Null(result.Value);
        Assert.Empty(await _harness.Service.GetRecentAsync());
    }

    /// <summary>
    /// More than one problem collapses to a count rather than to the first message alone; the
    /// screen shows the full list from <see cref="Core.Validation.ValidationResult.ToSummary"/>.
    /// </summary>
    [Fact]
    public async Task SeveralProblemsAtOnce_AreAllReported()
    {
        var result = await _harness.Executor.ExecuteAsync(new CreateWeighmentCommand(
            _harness.Service,
            new NewWeighment
            {
                VehicleNumber = " ",
                Mode = (WeighmentMode)9,
            }));

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(2, result.Validation!.Blocking.Count());
        Assert.Contains("2 problems", result.Message!, StringComparison.Ordinal);
        Assert.Contains("Enter the vehicle number.", result.Validation.ToSummary(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadOnly_CannotOpenAWeighment()
    {
        _harness.SignInAs(Roles.ReadOnly);

        var result = await Open();

        Assert.Equal(CommandOutcome.Denied, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));

        // Denial happens before execution, so nothing reached the database.
        _harness.SignInAs(Roles.Administrator);
        Assert.Empty(await _harness.Service.GetRecentAsync());
    }

    [Fact]
    public async Task ReadOnly_CannotRecordAWeight()
    {
        var opened = await Open();
        _harness.SignInAs(Roles.ReadOnly);

        var result = await First(opened.Value!.Id, 32_500m);

        Assert.Equal(CommandOutcome.Denied, result.Outcome);

        _harness.SignInAs(Roles.Administrator);
        var untouched = await _harness.Service.GetAsync(opened.Value.Id);
        Assert.Equal(WeighmentStatus.Created, untouched!.Status);
        Assert.Null(untouched.FirstWeight);
    }

    /// <summary>
    /// An operator may weigh all day and cannot make a started weighment disappear. That is
    /// the whole reason cancelling has its own permission.
    /// </summary>
    [Fact]
    public async Task AnOperator_CanWeigh_ButCannotCancel()
    {
        var opened = await Open();
        _harness.SignInAs(Roles.Operator);

        var weighed = await First(opened.Value!.Id, 32_500m);
        Assert.Equal(CommandOutcome.Succeeded, weighed.Outcome);

        var cancelled = await Cancel(opened.Value.Id, "Changed my mind");
        Assert.Equal(CommandOutcome.Denied, cancelled.Outcome);

        _harness.SignInAs(Roles.Administrator);
        var stillOpen = await _harness.Service.GetAsync(opened.Value.Id);
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, stillOpen!.Status);
        Assert.Null(stillOpen.CancellationReason);
    }

    [Fact]
    public async Task AnAdministrator_CanCancel_AndTheReasonIsKept()
    {
        var opened = await Open();

        var result = await Cancel(opened.Value!.Id, "Driver left without the second weighing");

        Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
        Assert.Contains("WB-000001", result.Message!, StringComparison.Ordinal);

        var cancelled = await _harness.Service.GetAsync(opened.Value.Id);
        Assert.Equal(WeighmentStatus.Cancelled, cancelled!.Status);
        Assert.Equal("Driver left without the second weighing", cancelled.CancellationReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CancellingWithoutAReason_FailsValidation(string reason)
    {
        var opened = await Open();

        var result = await Cancel(opened.Value!.Id, reason);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("reason", result.Message!, StringComparison.OrdinalIgnoreCase);

        var stillOpen = await _harness.Service.GetAsync(opened.Value.Id);
        Assert.Equal(WeighmentStatus.Created, stillOpen!.Status);
    }

    [Fact]
    public async Task CancellingAFinishedWeighment_FailsValidation()
    {
        var opened = await Open();
        await First(opened.Value!.Id, 32_500m);
        await Second(opened.Value.Id, 12_250m);

        var result = await Cancel(opened.Value.Id, "Too late");

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Equal(WeighmentStatus.Completed, (await _harness.Service.GetAsync(opened.Value.Id))!.Status);
    }

    /// <summary>
    /// The aggregate throws at this, on purpose. The command checks the same thing first so
    /// the operator gets a sentence about the stage rather than a failed operation.
    /// </summary>
    [Fact]
    public async Task TheSecondWeightBeforeTheFirst_FailsValidation_NotExecution()
    {
        var opened = await Open();

        var result = await Second(opened.Value!.Id, 12_250m);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Null(result.Error);
        Assert.Contains("has not been recorded yet", result.Message!, StringComparison.Ordinal);
        Assert.Equal(WeighmentStatus.Created, (await _harness.Service.GetAsync(opened.Value.Id))!.Status);
    }

    [Fact]
    public async Task TheFirstWeightTwice_FailsValidation_AndSaysWhatToDoInstead()
    {
        var opened = await Open();
        await First(opened.Value!.Id, 32_500m);

        var result = await First(opened.Value.Id, 31_000m);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("Record the second weight instead", result.Message!, StringComparison.Ordinal);

        // The reading that was accepted stands.
        var unchanged = await _harness.Service.GetAsync(opened.Value.Id);
        Assert.Equal(32_500m, unchanged!.FirstWeight!.Kilograms);
    }

    [Fact]
    public async Task ANetThatIsNotPositive_FailsValidation_AndNamesBothFigures()
    {
        var opened = await Open(mode: WeighmentMode.GrossFirst);
        await First(opened.Value!.Id, 12_000m);

        // A tare heavier than the gross: either the weighing is wrong or the arrival mode is.
        var result = await Second(opened.Value.Id, 15_000m);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Contains("gross weight (12000 kg)", result.Message!, StringComparison.Ordinal);
        Assert.Contains("tare weight (15000 kg)", result.Message!, StringComparison.Ordinal);
        Assert.Contains("arrived loaded or empty", result.Message!, StringComparison.Ordinal);

        // Still workable: the operator re-weighs rather than starting again.
        var stillWaiting = await _harness.Service.GetAsync(opened.Value.Id);
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, stillWaiting!.Status);
        Assert.Null(stillWaiting.NetWeightKg);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    [InlineData(250_000)]
    public async Task AnImplausibleWeight_FailsValidation(int kilograms)
    {
        var opened = await Open();

        var result = await First(opened.Value!.Id, kilograms);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Null((await _harness.Service.GetAsync(opened.Value.Id))!.FirstWeight);
    }

    [Fact]
    public async Task AWeighmentThatIsNotThere_FailsValidation_RatherThanThrowing()
    {
        var result = await First(4_242L, 32_500m);

        Assert.Equal(CommandOutcome.ValidationFailed, result.Outcome);
        Assert.Null(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    /// <summary>
    /// The screen's status line binds straight to <see cref="CommandResult.Message"/>, so an
    /// outcome that arrived without one would be a silent failure in front of an operator.
    /// </summary>
    [Fact]
    public async Task EveryOutcome_ArrivesWithSomethingToShowTheOperator()
    {
        var succeeded = await Open();
        var invalid = await Open(vehicleNumber: " ");

        _harness.SignInAs(Roles.ReadOnly);
        var denied = await Open(vehicleNumber: "MH12AB0002");

        foreach (var result in new CommandResult[] { succeeded, invalid, denied })
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Message), $"{result.Outcome} carried no message.");
        }

        Assert.Equal(
            [CommandOutcome.Succeeded, CommandOutcome.ValidationFailed, CommandOutcome.Denied],
            new[] { succeeded.Outcome, invalid.Outcome, denied.Outcome });
    }

    /// <summary>
    /// Opening a new weighment for a vehicle that already has an open transaction must fail.
    /// </summary>
    [Fact]
    public async Task OpeningWeighment_ForVehicleWithPendingTransaction_Fails()
    {
        var first = await Open("MH12DUPLICATE");
        Assert.Equal(CommandOutcome.Succeeded, first.Outcome);

        var second = await Open("MH12DUPLICATE");
        Assert.Equal(CommandOutcome.Failed, second.Outcome);
        Assert.Contains("already has an open weighment", second.Message);
    }
}
