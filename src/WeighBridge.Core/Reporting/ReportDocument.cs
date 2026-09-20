namespace WeighBridge.Core.Reporting;

/// <summary>
/// Dedicated query filter parameters for generating canonical report documents across UI, Preview, Print, and Exports.
/// Text filters perform case-insensitive partial / contains matching.
/// Slip numbers accept canonical inputs normalized to sequence integers.
/// </summary>
public sealed record ReportFilterParameters(
    long? StartSlipSequence = null,
    long? EndSlipSequence = null,
    DateTime? StartDateLocal = null,
    DateTime? EndDateLocal = null,
    string? VehicleNumber = null,
    string? PartyName = null,
    string? MaterialName = null,
    string? VehicleTypeName = null);

/// <summary>
/// Canonical in-memory report document that acts as the single source of truth for:
/// Preview, WPF Print, PDF export, Excel export, and Email dispatch.
/// </summary>
public sealed record ReportDocument(
    string Title,
    string CompanyName,
    DateTime StartDateLocal,
    DateTime EndDateLocal,
    IReadOnlyList<ReportDocumentRow> Rows,
    int TotalRecordCount,
    decimal TotalNetWeightKg,
    decimal TotalCharges,
    bool IsRowLimited = false,
    int? MaxRows = null,
    string AddressLine1 = "",
    string AddressLine2 = "")
{
    public static ReportDocument Create(
        string title,
        string companyName,
        DateTime startDateLocal,
        DateTime endDateLocal,
        IReadOnlyList<ReportDocumentRow> rows,
        bool isRowLimited = false,
        int? maxRows = null,
        string addressLine1 = "",
        string addressLine2 = "")
    {
        var totalRecords = rows.Count;
        var totalNetWeight = rows.Sum(r => r.NetWeightKg);
        var totalCharges = rows.Sum(r => r.TotalCharges);

        return new ReportDocument(
            title,
            companyName,
            startDateLocal,
            endDateLocal,
            rows,
            totalRecords,
            totalNetWeight,
            totalCharges,
            isRowLimited,
            maxRows,
            addressLine1,
            addressLine2);
    }
}

/// <summary>
/// One tabular row in a canonical ReportDocument.
/// </summary>
public sealed record ReportDocumentRow(
    int SerialNumber,
    string SlipNumber,
    string VehicleNumber,
    string VehicleTypeName,
    string PartyName,
    string MaterialName,
    decimal Charges1,
    decimal Charges2,
    decimal TotalCharges,
    decimal GrossWeightKg,
    decimal TareWeightKg,
    decimal NetWeightKg,
    DateTime? GrossCapturedAtLocal,
    DateTime? CompletedAtLocal,
    string Status,
    DateTime? TareCapturedAtLocal = null);
