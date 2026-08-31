namespace WeighBridge.Core.Reporting;

/// <summary>
/// Dedicated read-model projection for tabular reporting and CSV/Excel export queries.
/// Kept strictly separated from the single-ticket print DTO (<see cref="WeighBridge.Core.Printing.WeighmentPrintData"/>).
/// </summary>
public sealed record WeighmentReportRow(
    long Id,
    string SlipNumber,
    string VehicleNumber,
    string? VehicleTypeName,
    string? PartyName,
    string? MaterialName,
    string? DriverName,
    string? TransporterName,
    string? GatePassNumber,
    decimal GrossWeightKg,
    DateTime? GrossCapturedAtLocal,
    decimal TareWeightKg,
    DateTime? TareCapturedAtLocal,
    decimal NetWeightKg,
    int? NumberOfBags,
    decimal? BagWeightKg,
    decimal? TotalBagWeightKg,
    decimal? ActualWeightKg,
    decimal Charges,
    decimal SecondCharges,
    decimal TotalCharges,
    DateTime CreatedAtLocal,
    DateTime? CompletedAtLocal,
    string Status,
    string? Remarks
);
