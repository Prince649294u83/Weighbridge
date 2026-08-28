using WeighBridge.Core.Validation;

namespace WeighBridge.Tests.Validation;

/// <summary>
/// Covers the validation framework: rule ordering, severity, property scoping,
/// cancellation and the merge/summary helpers the pipeline relies on.
/// </summary>
public sealed class ValidatorTests
{
    private sealed record Vehicle(string Number, int Capacity);

    /// <summary>A validator shaped the way a real one will be, with no domain rules in it.</summary>
    private sealed class VehicleValidator : Validator<Vehicle>
    {
        public VehicleValidator()
        {
            AddRule(
                nameof(Vehicle.Number),
                vehicle => !string.IsNullOrWhiteSpace(vehicle.Number),
                "A vehicle number is required.",
                code: "Number.Required");

            AddRule(
                nameof(Vehicle.Capacity),
                vehicle => vehicle.Capacity > 0,
                "Capacity must be greater than zero.",
                code: "Capacity.Positive");

            AddRule(
                nameof(Vehicle.Capacity),
                vehicle => vehicle.Capacity <= 60,
                "Capacity above 60 tonnes is unusual.",
                ValidationSeverity.Warning,
                "Capacity.Unusual");
        }
    }

    [Fact]
    public async Task ValidateAsync_ValidInstance_ReportsNoErrors()
    {
        var result = await new VehicleValidator().ValidateAsync(new Vehicle("MH12AB1234", 25));

        Assert.True(result.IsValid);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public async Task ValidateAsync_CollectsEveryFailureRatherThanStoppingAtTheFirst()
    {
        var result = await new VehicleValidator().ValidateAsync(new Vehicle(string.Empty, 0));

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.Code == "Number.Required");
        Assert.Contains(result.Errors, error => error.Code == "Capacity.Positive");
    }

    [Fact]
    public async Task ValidateAsync_WarningDoesNotBlock()
    {
        var result = await new VehicleValidator().ValidateAsync(new Vehicle("MH12AB1234", 80));

        // A warning is reported and the instance is still valid: the operator is told the
        // capacity looks odd, not prevented from saving a legitimately large tanker.
        Assert.True(result.IsValid);
        Assert.False(result.IsEmpty);
        Assert.Single(result.Errors);
        Assert.Empty(result.Blocking);
    }

    [Fact]
    public async Task ValidatePropertyAsync_RunsOnlyThatPropertysRules()
    {
        var result = await new VehicleValidator()
            .ValidatePropertyAsync(new Vehicle(string.Empty, 0), nameof(Vehicle.Capacity));

        // The empty number is invalid too, but as-you-type validation of one field must not
        // light up every other field the operator has not reached yet.
        Assert.Single(result.Errors);
        Assert.Equal("Capacity.Positive", result.Errors[0].Code);
    }

    [Fact]
    public async Task ValidateAsync_RunsRulesInDeclarationOrder()
    {
        var result = await new VehicleValidator().ValidateAsync(new Vehicle(string.Empty, 0));

        Assert.Equal(nameof(Vehicle.Number), result.Errors[0].PropertyName);
        Assert.Equal(nameof(Vehicle.Capacity), result.Errors[1].PropertyName);
    }

    [Fact]
    public async Task ValidateAsync_CancelledToken_Throws()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new VehicleValidator().ValidateAsync(new Vehicle("MH12AB1234", 25), cancellation.Token));
    }

    [Fact]
    public async Task ValidateAsync_WrongType_ThrowsRatherThanReportingInvalid()
    {
        IValidator validator = new VehicleValidator();

        // A wiring mistake must not masquerade as bad operator input.
        await Assert.ThrowsAsync<ArgumentException>(() => validator.ValidateAsync("not a vehicle"));
    }

    [Fact]
    public async Task ValidateAsync_ThroughNonGenericInterface_Works()
    {
        IValidator validator = new VehicleValidator();

        var result = await validator.ValidateAsync(new Vehicle(string.Empty, 5));

        Assert.False(result.IsValid);
        Assert.Equal(typeof(Vehicle), validator.ValidatedType);
    }

    [Fact]
    public async Task ValidationContext_CarriesModeToRules()
    {
        var seen = new List<ValidationMode>();

        var validator = new DelegateValidator(context =>
        {
            seen.Add(context.Mode);
            return true;
        });

        await validator.ValidateAsync(new Vehicle("MH12AB1234", 25), new ValidationContext(ValidationMode.Update));

        Assert.Equal([ValidationMode.Update], seen);
    }

    private sealed class DelegateValidator : Validator<Vehicle>
    {
        public DelegateValidator(Func<ValidationContext, bool> predicate)
            => AddRule(
                propertyName: null,
                (_, context) => Task.FromResult(predicate(context)),
                _ => "Rule failed.");
    }
}
