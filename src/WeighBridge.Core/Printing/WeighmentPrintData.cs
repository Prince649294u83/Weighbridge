namespace WeighBridge.Core.Printing;

/// <summary>
/// Immutable, canonical data contract for weighment slip printing, duplication, and messaging payloads.
/// Contains presentation-ready values extracted directly from the authoritative <see cref="WeighBridge.Domain.Weighments.Weighment"/> aggregate
/// without independently recalculating business semantics.
/// </summary>
public sealed record WeighmentPrintData(
    long WeighmentId,
    string SlipNumber,
    string VehicleNumber,
    string? VehicleTypeName,
    string? PartyName,
    string? MaterialName,
    string? DriverName,
    string? TransporterName,
    string? GatePassNumber,
    string? CustomField1,
    string? CustomField2,
    string? CustomField3,
    string? CustomField4,
    decimal GrossWeightKg,
    DateTime? GrossCapturedAtLocal,
    decimal TareWeightKg,
    DateTime? TareCapturedAtLocal,
    decimal NetWeightKg,
    int? NumberOfBags,
    decimal? BagWeightKg,
    decimal? TotalBagWeightKg,
    decimal? ActualWeightKg,
    decimal FirstCharges,
    decimal SecondCharges,
    decimal TotalCharges,
    DateTime OpenedAtLocal,
    DateTime? CompletedAtLocal,
    string OperatorUsername,
    string OperatorDisplayName,
    string? Remarks,
    bool IsDuplicate,
    string? DuplicateWatermarkText = null,
    string CompanyName = "",
    string AddressLine1 = "",
    string AddressLine2 = "",
    string Phone = "",
    string Email = "",
    string TaxId = ""
);
