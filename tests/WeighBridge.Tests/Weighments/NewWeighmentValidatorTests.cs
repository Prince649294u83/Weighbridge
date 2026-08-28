using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Validation;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;
using WeighBridge.Services.Weighments;

namespace WeighBridge.Tests.Weighments;

/// <summary>
/// What the operator can fix, reported all at once and against the field at fault.
/// </summary>
/// <remarks>
/// The split being tested is the one the module is built on: this validator covers mistakes
/// worth pointing at a text box about, and <see cref="WeighmentTests"/> covers the invariants
/// that throw. A rule appearing in both places would be one rule with two implementations
/// waiting to disagree.
/// </remarks>
public sealed class NewWeighmentValidatorTests
{
    private static readonly NewWeighmentValidator Validator = new();

    private static Task<ValidationResult> Validate(NewWeighment request) => Validator.ValidateAsync(request);

    private static NewWeighment Valid() => new() { VehicleNumber = "MH12AB1234", Mode = WeighmentMode.GrossFirst };

    [Fact]
    public async Task AWellFormedRequest_Passes()
    {
        var result = await Validate(Valid() with
        {
            PartyName = "Acme Cement",
            MaterialName = "Clinker",
            DriverName = "R. Singh",
            TransporterName = "Blue Line",
            Remarks = "Gate 2",
        });

        Assert.True(result.IsValid);
        Assert.True(result.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMissingRegistration_IsReportedAgainstItsField(string? vehicleNumber)
    {
        var result = await Validate(Valid() with { VehicleNumber = vehicleNumber! });

        Assert.False(result.IsValid);
        var error = Assert.Single(result.ForProperty(nameof(NewWeighment.VehicleNumber)));
        Assert.Equal("Enter the vehicle number.", error.Message);
    }

    /// <summary>
    /// Normalisation strips spaces, dashes and dots, so a registration of <c>"- . -"</c>
    /// passes the not-blank rule and then reaches the column as an empty string.
    /// </summary>
    [Fact]
    public async Task ARegistrationOfNothingButPunctuation_IsRejected()
    {
        var result = await Validate(Valid() with { VehicleNumber = "- . -" });

        Assert.False(result.IsValid);
        Assert.Contains(
            "at least one letter or digit",
            Assert.Single(result.ForProperty(nameof(NewWeighment.VehicleNumber))).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARegistrationTooLongForTheColumn_IsRejected()
    {
        var result = await Validate(Valid() with { VehicleNumber = new string('A', Weighment.VehicleNumberMaxLength + 1) });

        Assert.False(result.IsValid);
        Assert.Contains(
            $"longer than {Weighment.VehicleNumberMaxLength}",
            Assert.Single(result.ForProperty(nameof(NewWeighment.VehicleNumber))).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The length rules test the normalised value, because that is what reaches the column.
    /// Twenty-four characters plus the spaces an operator typed between them still fits.
    /// </summary>
    [Fact]
    public async Task ARegistrationThatOnlyFitsOnceNormalised_IsAccepted()
    {
        var result = await Validate(Valid() with { VehicleNumber = "MH 12 AB 1234 - TRAILER" });

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task AnArrivalModeThatIsNotAnOption_IsRejected()
    {
        var result = await Validate(Valid() with { Mode = (WeighmentMode)7 });

        Assert.False(result.IsValid);
        Assert.Contains(
            "loaded or empty",
            Assert.Single(result.ForProperty(nameof(NewWeighment.Mode))).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextTooLongForItsColumn_IsRejectedPerField()
    {
        var result = await Validate(Valid() with
        {
            PartyName = new string('P', Weighment.NameMaxLength + 1),
            Remarks = new string('R', Weighment.TextMaxLength + 1),
        });

        Assert.False(result.IsValid);
        Assert.Single(result.ForProperty(nameof(NewWeighment.PartyName)));
        Assert.Single(result.ForProperty(nameof(NewWeighment.Remarks)));
        Assert.Empty(result.ForProperty(nameof(NewWeighment.MaterialName)));
    }

    /// <summary>
    /// Every finding at once. An operator who fixes one field, submits, and is told about the
    /// next one is making a round trip per mistake.
    /// </summary>
    [Fact]
    public async Task EveryProblem_IsReportedInOnePass()
    {
        var result = await Validate(new NewWeighment
        {
            VehicleNumber = " ",
            Mode = (WeighmentMode)9,
            DriverName = new string('D', Weighment.NameMaxLength + 1),
        });

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Blocking.Count());
        Assert.Contains(Environment.NewLine, result.ToSummary(), StringComparison.Ordinal);
    }
}
