using WeighBridge.App.Controls;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.App.ViewModels;

/// <summary>
/// One weighment as the Vehicle Entry screen shows it.
/// </summary>
/// <remarks>
/// <para>
/// Primitives only — deliberately not a wrapper around a <see cref="Weighment"/>. The screen
/// binds a list to it and a selection back out of it, and a record of primitives has value
/// equality, so re-reading the list after a save leaves the operator's selection pointing at
/// the same row rather than at a stale instance the change tracker has since replaced.
/// </para>
/// <para>
/// The formatting lives here rather than in converters because it is this screen's phrasing,
/// not a reusable rule. The domain's own <see cref="Weighment.NextAction"/> text is
/// carried through unchanged so the screen and the record agree on what happens next.
/// </para>
/// </remarks>
public sealed record WeighmentSummary(
    long Id,
    string SlipNumber,
    string VehicleNumber,
    WeighmentStatus Status,
    WeighmentMode Mode,
    string NextAction,
    string? PartyName,
    string? MaterialName,
    string? DriverName,
    string? TransporterName,
    string? Remarks,
    decimal? GrossKg,
    decimal? TareKg,
    decimal? NetKg,
    decimal? FirstWeightKg,
    bool HasManualWeight,
    WeightSource? FirstWeightSource,
    WeightSource? SecondWeightSource,
    DateTime OpenedAtLocal,
    DateTime? ClosedAtLocal,
    string? CancellationReason,
    Guid Version,
    decimal Charges,
    decimal SecondCharges,
    decimal TotalCharges,
    int? NumberOfBags,
    decimal? BagWeightKg,
    decimal? TotalBagWeightKg,
    decimal? ActualWeightKg,
    string? GatePassNumber,
    string? CustomField1,
    string? CustomField2,
    string? CustomField3,
    string? CustomField4,
    string? VehicleTypeName,
    string? CreatedBy,
    string? ModifiedBy,
    DateTime? FirstWeightTimeLocal,
    string WaitingDurationText,
    DateTime? GrossWeightTimeLocal = null,
    DateTime? TareWeightTimeLocal = null)
{
    private const string WeightFormat = "#,##0.##";
    private const string CurrencyFormat = "N2";

    /// <summary>Shown where a weight has not been taken yet.</summary>
    private const string Absent = "—";

    /// <summary>Projects a saved weighment. Times become local, because operators read clocks.</summary>
    public static WeighmentSummary From(Weighment weighment)
    {
        ArgumentNullException.ThrowIfNull(weighment);

        var closedAtUtc = weighment.CompletedAtUtc ?? weighment.CancelledAtUtc;
        var firstWeightTimeUtc = weighment.FirstWeight?.CapturedAtUtc;

        // Calculate waiting duration for pending tickets
        string waitingDuration = Absent;
        if (weighment.Status == WeighmentStatus.AwaitingSecondWeight && firstWeightTimeUtc.HasValue)
        {
            var span = DateTime.UtcNow - firstWeightTimeUtc.Value;
            if (span.TotalMinutes < 1)
            {
                waitingDuration = "< 1m";
            }
            else if (span.TotalHours < 1)
            {
                waitingDuration = $"{(int)span.TotalMinutes}m";
            }
            else if (span.TotalDays < 1)
            {
                waitingDuration = $"{(int)span.TotalHours}h {span.Minutes}m";
            }
            else
            {
                waitingDuration = $"{(int)span.TotalDays}d {span.Hours}h";
            }
        }

        return new WeighmentSummary(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            weighment.Status,
            weighment.Mode,
            weighment.NextAction,
            weighment.PartyName,
            weighment.MaterialName,
            weighment.DriverName,
            weighment.TransporterName,
            weighment.Remarks,
            weighment.Gross?.Kilograms,
            weighment.Tare?.Kilograms,
            weighment.NetWeightKg,
            weighment.FirstWeight?.Kilograms,
            (weighment.FirstWeight?.IsManual ?? false) || (weighment.SecondWeight?.IsManual ?? false),
            weighment.FirstWeight?.Source,
            weighment.SecondWeight?.Source,
            weighment.CreatedAtUtc.ToLocalTime(),
            closedAtUtc?.ToLocalTime(),
            weighment.CancellationReason,
            weighment.Version,
            weighment.Charges,
            weighment.SecondCharges,
            weighment.Charges + weighment.SecondCharges,
            weighment.NumberOfBags,
            weighment.BagWeightKg,
            weighment.TotalBagWeightKg,
            weighment.ActualWeightKg,
            weighment.GatePassNumber,
            weighment.CustomField1,
            weighment.CustomField2,
            weighment.CustomField3,
            weighment.CustomField4,
            weighment.VehicleTypeName,
            weighment.CreatedBy,
            weighment.ModifiedBy,
            firstWeightTimeUtc?.ToLocalTime(),
            waitingDuration,
            weighment.Gross?.CapturedAtUtc.ToLocalTime(),
            weighment.Tare?.CapturedAtUtc.ToLocalTime());
    }

    /// <summary>True while the weighment can still be worked on.</summary>
    public bool IsOpen => Status is WeighmentStatus.Created or WeighmentStatus.AwaitingSecondWeight;

    /// <summary>True when the vehicle has been weighed once and is expected back.</summary>
    public bool IsAwaitingSecondWeight => Status == WeighmentStatus.AwaitingSecondWeight;

    /// <summary>
    /// Whether the weight the operator is about to enter is the gross.
    /// </summary>
    public bool NeedsGrossNext => Status == WeighmentStatus.Created
        ? Mode == WeighmentMode.GrossFirst
        : Mode == WeighmentMode.TareFirst;

    /// <summary>Label for the weight entry field at this stage.</summary>
    public string WeightLabel => NeedsGrossNext ? "Gross weight (kg)" : "Tare weight (kg)";

    /// <summary>Label for the button that records the weight at this stage.</summary>
    public string ActionLabel => Status == WeighmentStatus.Created
        ? "Record first weight"
        : "Record second weight";

    /// <summary>Status in the words the operator uses, not the enum's.</summary>
    public string StatusText => Status switch
    {
        WeighmentStatus.Created => "First weight pending",
        WeighmentStatus.AwaitingSecondWeight => "Awaiting second weight",
        WeighmentStatus.Completed => "Completed",
        WeighmentStatus.Cancelled => "Cancelled",
        _ => Status.ToString(),
    };

    /// <summary>How the status badge should read.</summary>
    public BadgeSeverity StatusSeverity => Status switch
    {
        WeighmentStatus.Created => BadgeSeverity.Information,
        WeighmentStatus.AwaitingSecondWeight => BadgeSeverity.Warning,
        WeighmentStatus.Completed => BadgeSeverity.Success,
        WeighmentStatus.Cancelled => BadgeSeverity.Danger,
        _ => BadgeSeverity.Neutral,
    };

    /// <summary>Whether the vehicle arrived loaded or empty, spelled out.</summary>
    public string ModeText => Mode == WeighmentMode.GrossFirst
        ? "Arrived loaded — gross first"
        : "Arrived empty — tare first";

    /// <summary>Short mode indicator code (G for Gross-first, T for Tare-first).</summary>
    public string ModeCode => Mode == WeighmentMode.GrossFirst ? "G" : "T";

    /// <summary>Gross weight, or a dash.</summary>
    public string GrossText => Format(GrossKg);

    /// <summary>Tare weight, or a dash.</summary>
    public string TareText => Format(TareKg);

    /// <summary>Net weight, or a dash.</summary>
    public string NetText => Format(NetKg);

    /// <summary>The weight taken first, whichever it was, or a dash.</summary>
    public string FirstWeightText => Format(FirstWeightKg);

    /// <summary>Gross weight capture date text.</summary>
    public string GrossWeightDateText => GrossWeightTimeLocal?.ToString("dd-MM-yyyy") ?? Absent;

    /// <summary>Gross weight capture time text.</summary>
    public string GrossWeightTimeText => GrossWeightTimeLocal?.ToString("hh:mm tt") ?? Absent;

    /// <summary>Tare weight capture date text.</summary>
    public string TareWeightDateText => TareWeightTimeLocal?.ToString("dd-MM-yyyy") ?? Absent;

    /// <summary>Tare weight capture time text.</summary>
    public string TareWeightTimeText => TareWeightTimeLocal?.ToString("hh:mm tt") ?? Absent;

    /// <summary>Gross weight capture full date and time text.</summary>
    public string GrossWeightDateTimeText => GrossWeightTimeLocal.HasValue ? GrossWeightTimeLocal.Value.ToString("dd-MM-yyyy hh:mm tt") : Absent;

    /// <summary>Tare weight capture full date and time text.</summary>
    public string TareWeightDateTimeText => TareWeightTimeLocal.HasValue ? TareWeightTimeLocal.Value.ToString("dd-MM-yyyy hh:mm tt") : Absent;

    /// <summary>Formatted gross weight in kilograms.</summary>
    public string FormattedGrossKg => GrossKg.HasValue ? $"{GrossKg.Value:N0} Kg" : Absent;

    /// <summary>Formatted tare weight in kilograms.</summary>
    public string FormattedTareKg => TareKg.HasValue ? $"{TareKg.Value:N0} Kg" : Absent;

    /// <summary>Formatted net weight in kilograms.</summary>
    public string FormattedNetKg => NetKg.HasValue ? $"{NetKg.Value:N0} Kg" : Absent;

    /// <summary>True when bag deduction is configured.</summary>
    public bool HasBagDeduction => TotalBagWeightKg.HasValue && TotalBagWeightKg.Value > 0;

    /// <summary>Total packaging bag deduction weight, or a dash.</summary>
    public string BagDeductionText => TotalBagWeightKg.HasValue ? Format(TotalBagWeightKg.Value) : Absent;

    /// <summary>Actual material weight after bag deductions, or a dash.</summary>
    public string ActualWeightText => ActualWeightKg.HasValue ? Format(ActualWeightKg.Value) : Absent;

    /// <summary>Formatted charges in rupees.</summary>
    public string ChargesText => Charges > 0 ? $"₹ {Charges.ToString(CurrencyFormat)}" : Absent;

    /// <summary>Formatted second charges in rupees.</summary>
    public string SecondChargesText => SecondCharges > 0 ? $"₹ {SecondCharges.ToString(CurrencyFormat)}" : Absent;

    /// <summary>Formatted total charges in rupees.</summary>
    public string TotalChargesText => TotalCharges > 0 ? $"₹ {TotalCharges.ToString(CurrencyFormat)}" : Absent;

    /// <summary>When the weighment was opened.</summary>
    public string OpenedAtText => OpenedAtLocal.ToString("dd MMM yyyy HH:mm");

    /// <summary>When the first weight was captured.</summary>
    public string FirstWeightTimeText => FirstWeightTimeLocal?.ToString("dd MMM yyyy HH:mm") ?? Absent;

    /// <summary>Party, material and driver on one line, skipping whatever was left blank.</summary>
    public string Details => string.Join(
        " · ",
        new[] { PartyName, MaterialName, DriverName }.Where(value => !string.IsNullOrWhiteSpace(value)));

    /// <summary>
    /// Whether to state where the weight came from.
    /// </summary>
    public bool ShowProvenance => FirstWeightKg.HasValue;

    /// <summary>Where the weight came from, in a sentence an auditor can read.</summary>
    public string ProvenanceText
    {
        get
        {
            if (FirstWeightSource == WeightSource.Simulator || SecondWeightSource == WeightSource.Simulator)
            {
                return "Weight simulated (test mode)";
            }

            return HasManualWeight
                ? "Weight typed by operator"
                : "Weight read from indicator";
        }
    }

    private static string Format(decimal? kilograms)
        => kilograms.HasValue ? kilograms.Value.ToString(WeightFormat) : Absent;
}

/// <summary>One entry in the arrival-mode picker.</summary>
/// <param name="Value">The mode this option selects.</param>
/// <param name="Text">How it reads on screen.</param>
public sealed record WeighmentModeOption(WeighmentMode Value, string Text)
{
    public override string ToString() => Text;
}
