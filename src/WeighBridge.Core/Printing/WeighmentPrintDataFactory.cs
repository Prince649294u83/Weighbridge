using WeighBridge.Core.Configuration;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Core.Printing;

/// <summary>
/// Factory that constructs immutable <see cref="WeighmentPrintData"/> DTOs from authoritative
/// <see cref="Weighment"/> domain aggregates and configuration headers.
/// </summary>
public static class WeighmentPrintDataFactory
{
    /// <summary>
    /// Creates a presentation-ready <see cref="WeighmentPrintData"/> contract directly from the
    /// authoritative <see cref="Weighment"/> aggregate and site configuration.
    /// </summary>
    /// <param name="weighment">Authoritative weighment aggregate.</param>
    /// <param name="companyOptions">Optional current company header configuration.</param>
    /// <param name="operatorUsername">Optional current operator username.</param>
    /// <param name="operatorDisplayName">Optional current operator display name.</param>
    /// <param name="isDuplicate">Whether this is a duplicate slip reprint.</param>
    /// <param name="duplicateWatermark">Watermark text for duplicate slips.</param>
    /// <returns>Immutable, calculation-free print data DTO.</returns>
    public static WeighmentPrintData Create(
        Weighment weighment,
        CompanyOptions? companyOptions = null,
        string? operatorUsername = null,
        string? operatorDisplayName = null,
        bool isDuplicate = false,
        string? duplicateWatermark = "WEIGHMENT SLIP (DUPLICATE)")
    {
        ArgumentNullException.ThrowIfNull(weighment);

        var effectiveUsername = !string.IsNullOrWhiteSpace(operatorUsername)
            ? operatorUsername
            : weighment.CreatedBy ?? "-";

        var effectiveDisplayName = !string.IsNullOrWhiteSpace(operatorDisplayName)
            ? operatorDisplayName
            : effectiveUsername;

        return new WeighmentPrintData(
            WeighmentId: weighment.Id,
            SlipNumber: weighment.SlipNumber,
            VehicleNumber: weighment.VehicleNumber,
            VehicleTypeName: weighment.VehicleTypeName,
            PartyName: weighment.PartyName,
            MaterialName: weighment.MaterialName,
            DriverName: weighment.DriverName,
            TransporterName: weighment.TransporterName,
            GatePassNumber: weighment.GatePassNumber,
            CustomField1: weighment.CustomField1,
            CustomField2: weighment.CustomField2,
            CustomField3: weighment.CustomField3,
            CustomField4: weighment.CustomField4,
            GrossWeightKg: weighment.Gross?.Kilograms ?? 0m,
            GrossCapturedAtLocal: weighment.Gross?.CapturedAtUtc.ToLocalTime(),
            TareWeightKg: weighment.Tare?.Kilograms ?? 0m,
            TareCapturedAtLocal: weighment.Tare?.CapturedAtUtc.ToLocalTime(),
            NetWeightKg: weighment.NetWeightKg ?? 0m,
            NumberOfBags: weighment.NumberOfBags,
            BagWeightKg: weighment.BagWeightKg,
            TotalBagWeightKg: weighment.TotalBagWeightKg,
            ActualWeightKg: weighment.ActualWeightKg,
            FirstCharges: weighment.Charges,
            SecondCharges: weighment.SecondCharges,
            TotalCharges: weighment.Charges + weighment.SecondCharges,
            OpenedAtLocal: weighment.CreatedAtUtc.ToLocalTime(),
            CompletedAtLocal: weighment.CompletedAtUtc?.ToLocalTime(),
            OperatorUsername: effectiveUsername,
            OperatorDisplayName: effectiveDisplayName,
            Remarks: weighment.Remarks,
            IsDuplicate: isDuplicate,
            DuplicateWatermarkText: isDuplicate ? duplicateWatermark : null,
            CompanyName: companyOptions?.CompanyName ?? string.Empty,
            AddressLine1: companyOptions?.AddressLine1 ?? string.Empty,
            AddressLine2: companyOptions?.AddressLine2 ?? string.Empty,
            Phone: companyOptions?.Phone ?? string.Empty,
            Email: companyOptions?.Email ?? string.Empty,
            TaxId: companyOptions?.TaxId ?? string.Empty
        );
    }
}
