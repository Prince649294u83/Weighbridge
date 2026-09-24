using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Tests.Weighments;

/// <summary>
/// The lifecycle and the arithmetic, with no database and no container in the way.
/// </summary>
/// <remarks>
/// These are the rules a slip is evidence of. Each test that expects a refusal also asserts
/// the state did not move: an invariant that throws but has already mutated the aggregate has
/// left behind exactly the record it was written to prevent.
/// </remarks>
public sealed class WeighmentTests
{
    private static WeightCapture Reading(decimal kilograms, WeightSource source = WeightSource.Indicator)
        => new(kilograms, DateTime.UtcNow, source);

    /// <summary>A weighment as it exists after the insert that allocates its identity.</summary>
    private static Weighment Saved(WeighmentMode mode, long id = 1)
    {
        var weighment = Weighment.Open("MH12AB1234", mode);
        weighment.Id = id;
        weighment.AssignSlipNumber();
        return weighment;
    }

    [Fact]
    public void Open_NormalisesTheRegistration_AndTakesNoWeightYet()
    {
        var weighment = Weighment.Open(
            " mh-12 ab 1234 ",
            WeighmentMode.GrossFirst,
            partyName: "  Acme Cement  ",
            materialName: "   ");

        Assert.Equal("MH12AB1234", weighment.VehicleNumber);
        Assert.Equal(WeighmentStatus.Created, weighment.Status);
        Assert.True(weighment.IsOpen);
        Assert.Null(weighment.FirstWeight);
        Assert.Null(weighment.SecondWeight);
        Assert.Null(weighment.NetWeightKg);
        Assert.Equal("Acme Cement", weighment.PartyName);

        // Whitespace is absence, not a value: a party of "   " on a slip is a blank field.
        Assert.Null(weighment.MaterialName);

        // The slip number is the database's to allocate, not this method's.
        Assert.Equal(string.Empty, weighment.SlipNumber);
        Assert.True(weighment.IsTransient);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_WithoutARegistration_IsRefused(string? vehicleNumber)
        => Assert.ThrowsAny<ArgumentException>(
            () => Weighment.Open(vehicleNumber!, WeighmentMode.GrossFirst));

    [Theory]
    [InlineData("mh 12 ab 1234")]
    [InlineData("MH-12-AB-1234")]
    [InlineData("mh.12.ab.1234")]
    [InlineData("  MH12AB1234  ")]
    public void NormaliseVehicleNumber_MakesTheSameLorryOneRegistration(string typed)
        => Assert.Equal("MH12AB1234", Weighment.NormaliseVehicleNumber(typed));

    [Fact]
    public void GrossFirst_CompletesAtGrossMinusTare()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        Assert.Equal("Record gross weight", weighment.NextAction);

        weighment.RecordFirstWeight(Reading(32_500m));

        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, weighment.Status);
        Assert.Equal("Record tare weight", weighment.NextAction);
        Assert.Equal(32_500m, weighment.Gross!.Kilograms);
        Assert.Null(weighment.Tare);
        Assert.Null(weighment.NetWeightKg);

        weighment.RecordSecondWeight(Reading(12_250.5m));

        Assert.Equal(32_500m, weighment.Gross!.Kilograms);
        Assert.Equal(12_250.5m, weighment.Tare!.Kilograms);
        Assert.Equal(20_249.5m, weighment.NetWeightKg);
        Assert.Equal(WeighmentStatus.Completed, weighment.Status);
        Assert.NotNull(weighment.CompletedAtUtc);
        Assert.False(weighment.IsOpen);
        Assert.Equal("Print slip", weighment.NextAction);
    }

    [Fact]
    public void TareFirst_ReachesTheSameNetFromTheOppositeOrder()
    {
        var weighment = Saved(WeighmentMode.TareFirst);
        Assert.Equal("Record tare weight", weighment.NextAction);

        weighment.RecordFirstWeight(Reading(12_250.5m));

        Assert.Equal("Record gross weight", weighment.NextAction);
        Assert.Equal(12_250.5m, weighment.Tare!.Kilograms);
        Assert.Null(weighment.Gross);

        weighment.RecordSecondWeight(Reading(32_500m));

        Assert.Equal(32_500m, weighment.Gross!.Kilograms);
        Assert.Equal(12_250.5m, weighment.Tare!.Kilograms);
        Assert.Equal(20_249.5m, weighment.NetWeightKg);
        Assert.Equal(WeighmentStatus.Completed, weighment.Status);
    }

    /// <summary>
    /// The reason the weights are <see cref="decimal"/>. In binary floating point
    /// <c>20000.3 - 10000.1</c> is 10000.199999999999, and that figure would be printed on a
    /// slip, invoiced, and then disagree with the customer's own arithmetic.
    /// </summary>
    [Fact]
    public void Net_IsExact_NotApproximate()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.RecordFirstWeight(Reading(20_000.3m));
        weighment.RecordSecondWeight(Reading(10_000.1m));

        Assert.Equal(10_000.2m, weighment.NetWeightKg);
        Assert.Equal(weighment.Gross!.Kilograms - weighment.Tare!.Kilograms, weighment.NetWeightKg);
    }

    [Fact]
    public void SecondWeight_BeforeTheFirst_IsRefused()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);

        var error = Assert.Throws<InvalidOperationException>(
            () => weighment.RecordSecondWeight(Reading(12_000m)));

        Assert.Contains("first weight has not been recorded", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WeighmentStatus.Created, weighment.Status);
        Assert.Null(weighment.SecondWeight);
        Assert.Null(weighment.NetWeightKg);
    }

    [Fact]
    public void FirstWeight_Twice_IsRefused()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.RecordFirstWeight(Reading(32_500m));

        Assert.Throws<InvalidOperationException>(() => weighment.RecordFirstWeight(Reading(31_000m)));

        // The reading that was accepted stands; the rejected one left no trace.
        Assert.Equal(32_500m, weighment.FirstWeight!.Kilograms);
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, weighment.Status);
    }

    [Fact]
    public void AThirdWeight_IsRefused()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.RecordFirstWeight(Reading(32_500m));
        weighment.RecordSecondWeight(Reading(12_250m));

        Assert.Throws<InvalidOperationException>(() => weighment.RecordSecondWeight(Reading(12_000m)));

        Assert.Equal(12_250m, weighment.SecondWeight!.Kilograms);
        Assert.Equal(20_250m, weighment.NetWeightKg);
    }

    [Theory]
    [InlineData(10_000, 10_000)] // A net of nothing is not a load.
    [InlineData(10_000, 12_000)] // The tare cannot outweigh the gross.
    public void ANetThatIsNotPositive_IsRefused(int grossKg, int tareKg)
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.RecordFirstWeight(Reading(grossKg));

        var error = Assert.Throws<InvalidOperationException>(
            () => weighment.RecordSecondWeight(Reading(tareKg)));

        Assert.Contains("must be greater than", error.Message, StringComparison.Ordinal);

        // Still workable: the operator re-weighs or fixes the arrival mode, and the record
        // is not left half-completed.
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, weighment.Status);
        Assert.Null(weighment.SecondWeight);
        Assert.Null(weighment.NetWeightKg);
        Assert.Null(weighment.CompletedAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(200_001)]
    public void AnImplausibleWeight_IsRefused(int kilograms)
    {
        var weighment = Saved(WeighmentMode.GrossFirst);

        Assert.Throws<ArgumentOutOfRangeException>(() => weighment.RecordFirstWeight(Reading(kilograms)));
        Assert.Equal(WeighmentStatus.Created, weighment.Status);
        Assert.Null(weighment.FirstWeight);
    }

    [Fact]
    public void TheHeaviestPlausibleWeight_IsAccepted()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);

        weighment.RecordFirstWeight(Reading(Weighment.MaximumWeightKg));

        Assert.Equal(Weighment.MaximumWeightKg, weighment.FirstWeight!.Kilograms);
    }

    [Fact]
    public void ManualEntry_IsRecordedAsSuch()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);

        weighment.RecordFirstWeight(Reading(32_500m, WeightSource.Manual));

        Assert.True(weighment.FirstWeight!.IsManual);
        Assert.Equal(WeightSource.Manual, weighment.FirstWeight.Source);
    }

    [Fact]
    public void Cancel_KeepsTheRecordAndTheReason()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.RecordFirstWeight(Reading(32_500m));

        weighment.Cancel("  Driver left without the second weighing  ");

        Assert.Equal(WeighmentStatus.Cancelled, weighment.Status);
        Assert.Equal("Driver left without the second weighing", weighment.CancellationReason);
        Assert.NotNull(weighment.CancelledAtUtc);
        Assert.False(weighment.IsOpen);

        // The weight that was taken is still on the record; cancelling is not erasing.
        Assert.Equal(32_500m, weighment.FirstWeight!.Kilograms);
        Assert.False(weighment.IsDeleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WithoutAReason_IsRefused(string? reason)
    {
        var weighment = Saved(WeighmentMode.GrossFirst);

        Assert.ThrowsAny<ArgumentException>(() => weighment.Cancel(reason!));
        Assert.Equal(WeighmentStatus.Created, weighment.Status);
    }

    [Fact]
    public void Cancel_AfterCompletion_IsRefused()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.RecordFirstWeight(Reading(32_500m));
        weighment.RecordSecondWeight(Reading(12_250m));

        var error = Assert.Throws<InvalidOperationException>(() => weighment.Cancel("Changed my mind"));

        Assert.Contains("complete", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WeighmentStatus.Completed, weighment.Status);
        Assert.Null(weighment.CancellationReason);
    }

    [Fact]
    public void Cancel_Twice_IsRefused()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.Cancel("Wrong vehicle");

        Assert.Throws<InvalidOperationException>(() => weighment.Cancel("Wrong again"));
        Assert.Equal("Wrong vehicle", weighment.CancellationReason);
    }

    [Fact]
    public void UpdateDetails_ReplacesWhatTheSlipWillCarry_WhileTheWeighmentIsOpen()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);

        weighment.UpdateDetails("Acme Cement", "Clinker", "R. Singh", "Blue Line", null);

        Assert.Equal("Acme Cement", weighment.PartyName);
        Assert.Equal("Clinker", weighment.MaterialName);
        Assert.Equal("R. Singh", weighment.DriverName);
        Assert.Equal("Blue Line", weighment.TransporterName);
        Assert.Null(weighment.Remarks);
    }

    [Fact]
    public void UpdateDetails_AfterCompletion_IsRefused()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.RecordFirstWeight(Reading(32_500m));
        weighment.RecordSecondWeight(Reading(12_250m));

        Assert.Throws<InvalidOperationException>(
            () => weighment.UpdateDetails("Someone else", null, null, null, null));
    }

    [Fact]
    public void AssignSlipNumber_BeforeTheDatabaseAllocatesAnIdentity_IsRefused()
    {
        var weighment = Weighment.Open("MH12AB1234", WeighmentMode.GrossFirst);

        var error = Assert.Throws<InvalidOperationException>(weighment.AssignSlipNumber);

        Assert.Contains("identity", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, weighment.SlipNumber);
    }

    [Fact]
    public void AssignSlipNumber_Twice_IsRefused()
    {
        var weighment = Saved(WeighmentMode.GrossFirst, id: 42);
        Assert.Equal("000042", weighment.SlipNumber);

        Assert.Throws<InvalidOperationException>(weighment.AssignSlipNumber);
        Assert.Equal("000042", weighment.SlipNumber);
    }

    [Fact]
    public void UpdateDetails_AfterFirstWeight_F1DetailsCannotBeChanged()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.RecordFirstWeight(Reading(32_500m));
        Assert.Equal(WeighmentStatus.AwaitingSecondWeight, weighment.Status);

        var ex = Assert.Throws<InvalidOperationException>(
            () => weighment.UpdateDetails("Someone else", null, null, null, null));
        Assert.Contains("locked once the first weight is recorded", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateSecondEntryDetails_Requires_AwaitingSecondWeight()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        Assert.Equal(WeighmentStatus.Created, weighment.Status);

        // Cannot update second entry details in Created state
        Assert.Throws<InvalidOperationException>(
            () => weighment.UpdateSecondEntryDetails(50m, 10, 0.5m, "GP-101", "Second remarks"));

        // Transitions to AwaitingSecondWeight
        weighment.RecordFirstWeight(Reading(32_500m));
        var v1 = weighment.Version;

        weighment.UpdateSecondEntryDetails(
            secondCharges: 75.5m,
            numberOfBags: 100,
            bagWeightKg: 0.5m,
            gatePassNumber: "GP-999",
            remarks: "Updated remark",
            customField3: "Custom3Val",
            customField4: "Custom4Val");

        Assert.Equal(75.5m, weighment.SecondCharges);
        Assert.Equal(100, weighment.NumberOfBags);
        Assert.Equal(0.5m, weighment.BagWeightKg);
        Assert.Equal(50m, weighment.TotalBagWeightKg);
        Assert.Equal("GP-999", weighment.GatePassNumber);
        Assert.Equal("Updated remark", weighment.Remarks);
        Assert.Equal("Custom3Val", weighment.CustomField3);
        Assert.Equal("Custom4Val", weighment.CustomField4);
        Assert.NotEqual(v1, weighment.Version);

        // After completion, cannot update second entry details
        weighment.RecordSecondWeight(Reading(12_250m));
        Assert.Equal(WeighmentStatus.Completed, weighment.Status);

        Assert.Throws<InvalidOperationException>(
            () => weighment.UpdateSecondEntryDetails(50m, 10, 0.5m, "GP-101", "Second remarks"));
    }

    [Fact]
    public void RecordSecondWeight_ZeroNet_When_Disallowed_Throws_InvalidOperationException()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.RecordFirstWeight(Reading(15_000m));

        var ex = Assert.Throws<InvalidOperationException>(
            () => weighment.RecordSecondWeight(Reading(15_000m), NetWeightPolicy.RejectZero));
        Assert.Contains("Zero net weight is disallowed by policy", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RecordSecondWeight_ZeroNet_When_Allowed_Succeeds()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        weighment.RecordFirstWeight(Reading(15_000m));

        weighment.RecordSecondWeight(Reading(15_000m), NetWeightPolicy.AllowZero);

        Assert.Equal(0m, weighment.NetWeightKg);
        Assert.Equal(0m, weighment.ActualWeightKg);
        Assert.Equal(WeighmentStatus.Completed, weighment.Status);
    }

    [Fact]
    public void BagDeductions_Calculates_TotalBagWeight_And_ActualWeight_Correctly()
    {
        var weighment = Weighment.Open(
            "MH12AB1234",
            WeighmentMode.GrossFirst,
            charges: 100m,
            numberOfBags: 200,
            bagWeightKg: 0.5m,
            gatePassNumber: "GP-001",
            customField1: "F1-Custom1",
            customField2: "F1-Custom2");
        weighment.Id = 1;
        weighment.AssignSlipNumber();

        Assert.Equal(100m, weighment.Charges);
        Assert.Equal(200, weighment.NumberOfBags);
        Assert.Equal(0.5m, weighment.BagWeightKg);
        Assert.Equal(100m, weighment.TotalBagWeightKg);
        Assert.Null(weighment.ActualWeightKg); // null before second weight
        Assert.Equal("GP-001", weighment.GatePassNumber);
        Assert.Equal("F1-Custom1", weighment.CustomField1);
        Assert.Equal("F1-Custom2", weighment.CustomField2);

        weighment.RecordFirstWeight(Reading(30_000m));
        weighment.RecordSecondWeight(Reading(10_000m)); // Net = 20,000 kg

        Assert.Equal(20_000m, weighment.NetWeightKg);
        Assert.Equal(100m, weighment.TotalBagWeightKg);
        Assert.Equal(19_900m, weighment.ActualWeightKg);
    }

    [Fact]
    public void BagDeductions_Exceeding_NetWeight_Throws_InvalidOperationException()
    {
        var weighment = Weighment.Open(
            "MH12AB1234",
            WeighmentMode.GrossFirst,
            numberOfBags: 1000,
            bagWeightKg: 50m); // Total bag weight = 50,000 kg
        weighment.Id = 1;
        weighment.AssignSlipNumber();

        weighment.RecordFirstWeight(Reading(30_000m));

        // Net = 10,000 kg, which is less than 50,000 kg bag weight
        var ex = Assert.Throws<InvalidOperationException>(
            () => weighment.RecordSecondWeight(Reading(20_000m)));
        Assert.Contains("exceeds the net weight", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Negative_Charges_Or_Bags_Throws_ArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Weighment.Open("MH12AB1234", WeighmentMode.GrossFirst, charges: -10m));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Weighment.Open("MH12AB1234", WeighmentMode.GrossFirst, numberOfBags: -5));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Weighment.Open("MH12AB1234", WeighmentMode.GrossFirst, bagWeightKg: -0.5m));

        var created = Saved(WeighmentMode.GrossFirst);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => created.UpdateDetails(null, null, null, null, null, charges: -1m));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => created.UpdateDetails(null, null, null, null, null, numberOfBags: -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => created.UpdateDetails(null, null, null, null, null, bagWeightKg: -1m));

        created.RecordFirstWeight(Reading(10_000m));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => created.UpdateSecondEntryDetails(secondCharges: -1m, null, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => created.UpdateSecondEntryDetails(0m, numberOfBags: -1, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => created.UpdateSecondEntryDetails(0m, null, bagWeightKg: -1m, null, null));
    }

    [Fact]
    public void VersionToken_Changes_On_Every_State_Transition()
    {
        var weighment = Saved(WeighmentMode.GrossFirst);
        var v0 = weighment.Version;
        Assert.NotEqual(Guid.Empty, v0);

        weighment.UpdateDetails("Party1", "Mat1", "Drv1", "Trans1", "Rem1");
        var v1 = weighment.Version;
        Assert.NotEqual(v0, v1);

        weighment.RecordFirstWeight(Reading(25_000m));
        var v2 = weighment.Version;
        Assert.NotEqual(v1, v2);

        weighment.UpdateSecondEntryDetails(20m, 10, 0.5m, "GP-1", "Rem2");
        var v3 = weighment.Version;
        Assert.NotEqual(v2, v3);

        weighment.RecordSecondWeight(Reading(10_000m));
        var v4 = weighment.Version;
        Assert.NotEqual(v3, v4);

        var cancelled = Saved(WeighmentMode.GrossFirst);
        var vc0 = cancelled.Version;
        cancelled.Cancel("Test cancellation");
        var vc1 = cancelled.Version;
        Assert.NotEqual(vc0, vc1);
    }
}

/// <summary>
/// The slip number format, tested where it is defined rather than at each screen that shows
/// one. A number an operator cannot type back in is a number they cannot look a slip up by.
/// </summary>
public sealed class SlipNumberTests
{
    [Theory]
    [InlineData(1, "000001")]
    [InlineData(42, "000042")]
    [InlineData(999_999, "999999")]
    [InlineData(1_000_000, "1000000")] // Widens rather than truncating.
    public void Format_PadsToSixDigitsAndThenGrows(long sequence, string expected)
        => Assert.Equal(expected, SlipNumbers.Format(sequence));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Format_RejectsASequenceThatCannotExist(long sequence)
        => Assert.Throws<ArgumentOutOfRangeException>(() => SlipNumbers.Format(sequence));

    [Fact]
    public void Format_AndTryParse_RoundTrip()
    {
        Assert.True(SlipNumbers.TryParse(SlipNumbers.Format(12_345), out var sequence));
        Assert.Equal(12_345, sequence);
    }

    [Theory]
    [InlineData("WB-000042")]
    [InlineData("wb-000042")]
    [InlineData("WB-42")]
    [InlineData("  wb-42  ")]
    [InlineData("42")]
    [InlineData("000042")]
    public void Normalise_AcceptsWhatAnOperatorActuallyTypes(string typed)
        => Assert.Equal("000042", SlipNumbers.Normalise(typed));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("WB-")]
    [InlineData("WB-abc")]
    [InlineData("WB-0")]
    [InlineData("-5")]
    [InlineData("MH12AB1234")]
    public void Normalise_RejectsWhatIsNotASlipNumber(string? typed)
        => Assert.Null(SlipNumbers.Normalise(typed));

    [Fact]
    public void TheFormat_FitsTheColumnItIsStoredIn()
        => Assert.True(SlipNumbers.Format(long.MaxValue).Length <= SlipNumbers.MaxLength);
}
