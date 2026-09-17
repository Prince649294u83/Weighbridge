using System.Globalization;
using System.Text;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Reporting;

namespace WeighBridge.Printing.Services;

/// <summary>
/// Renders the canonical report document into a deterministic monospace print payload.
/// </summary>
public static class ReportPrintTextRenderer
{
    private const int WideColumns = 160;
    private const int NarrowColumns = 96;

    public static string Render(
        ReportDocument document,
        string paperSize,
        bool sideWisePrinting,
        IDateTimeFormatter? dateTimeFormatter = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        int width = ResolveWidth(paperSize, sideWisePrinting);
        var sb = new StringBuilder();

        AppendCentered(sb, Sanitize(document.CompanyName), width);
        AppendCentered(sb, Sanitize(document.Title), width);
        AppendCentered(sb, $"Period: {document.StartDateLocal:dd-MM-yyyy} to {document.EndDateLocal:dd-MM-yyyy}", width);
        sb.AppendLine(new string('-', width));
        sb.AppendLine(BuildHeader(width));
        sb.AppendLine(new string('-', width));

        foreach (var row in document.Rows)
        {
            sb.AppendLine(BuildRow(row, width, dateTimeFormatter));
        }

        sb.AppendLine(new string('-', width));
        sb.AppendLine(BuildTotals(document, width));
        if (document.IsRowLimited && document.MaxRows is > 0)
        {
            sb.AppendLine($"ROW LIMIT APPLIED: Showing first {document.MaxRows.Value:N0} rows. Refine criteria for a complete report.");
        }

        return sb.ToString();
    }

    private static int ResolveWidth(string paperSize, bool sideWisePrinting)
    {
        if (sideWisePrinting)
        {
            return WideColumns;
        }

        return paperSize.Contains("Half", StringComparison.OrdinalIgnoreCase) ||
               paperSize.Contains("A5", StringComparison.OrdinalIgnoreCase)
            ? NarrowColumns
            : WideColumns;
    }

    private static string BuildHeader(int width)
    {
        return width >= WideColumns
            ? Fixed("S.No", 5) + Fixed("Ticket", 12) + Fixed("Vehicle", 14) + Fixed("Type", 12) +
              Fixed("Party", 24) + Fixed("Material", 18) + Right("Charges", 10) +
              Right("Gross", 12) + Right("Tare", 12) + Right("Net", 12) + Fixed("Date/Time", 23) + "Status"
            : Fixed("S", 3) + Fixed("Ticket", 11) + Fixed("Vehicle", 12) + Fixed("Party", 16) +
              Fixed("Material", 14) + Right("Chg", 8) + Right("Gross", 9) + Right("Tare", 9) + Right("Net", 9) + "Date";
    }

    private static string BuildRow(ReportDocumentRow row, int width, IDateTimeFormatter? dateTimeFormatter)
    {
        if (width >= WideColumns)
        {
            return Fixed(row.SerialNumber.ToString(CultureInfo.InvariantCulture), 5) +
                   Fixed(row.SlipNumber, 12) +
                   Fixed(row.VehicleNumber, 14) +
                   Fixed(row.VehicleTypeName, 12) +
                   Fixed(row.PartyName, 24) +
                   Fixed(row.MaterialName, 18) +
                   Right(row.TotalCharges.ToString("N0", CultureInfo.InvariantCulture), 10) +
                   Right(row.GrossWeightKg.ToString("N0", CultureInfo.InvariantCulture), 12) +
                   Right(row.TareWeightKg.ToString("N0", CultureInfo.InvariantCulture), 12) +
                   Right(row.NetWeightKg.ToString("N0", CultureInfo.InvariantCulture), 12) +
                   Fixed(FormatDateTime(row, dateTimeFormatter), 23) +
                   Sanitize(row.Status);
        }

        return Fixed(row.SerialNumber.ToString(CultureInfo.InvariantCulture), 3) +
               Fixed(row.SlipNumber, 11) +
               Fixed(row.VehicleNumber, 12) +
               Fixed(row.PartyName, 16) +
               Fixed(row.MaterialName, 14) +
               Right(row.TotalCharges.ToString("N0", CultureInfo.InvariantCulture), 8) +
               Right(row.GrossWeightKg.ToString("N0", CultureInfo.InvariantCulture), 9) +
               Right(row.TareWeightKg.ToString("N0", CultureInfo.InvariantCulture), 9) +
               Right(row.NetWeightKg.ToString("N0", CultureInfo.InvariantCulture), 9) +
               FormatDate(row);
    }

    private static string BuildTotals(ReportDocument document, int width)
    {
        var totals = $"Total Records: {document.TotalRecordCount:N0}    Total Charges: {document.TotalCharges:N0}    Total Net: {document.TotalNetWeightKg:N0} kg";
        return totals.Length > width ? totals[..width] : totals;
    }

    private static string FormatDateTime(ReportDocumentRow row, IDateTimeFormatter? dateTimeFormatter)
    {
        var timestamp = row.CompletedAtLocal ?? row.GrossCapturedAtLocal;
        return timestamp.HasValue
            ? dateTimeFormatter?.FormatDateTime(timestamp.Value) ?? timestamp.Value.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string FormatDate(ReportDocumentRow row)
        => (row.CompletedAtLocal ?? row.GrossCapturedAtLocal)?.ToString("dd-MM-yy", CultureInfo.InvariantCulture) ?? string.Empty;

    private static void AppendCentered(StringBuilder sb, string text, int width)
    {
        if (text.Length >= width)
        {
            sb.AppendLine(text[..width]);
            return;
        }

        sb.AppendLine(new string(' ', (width - text.Length) / 2) + text);
    }

    private static string Fixed(string? value, int width)
    {
        var text = Sanitize(value);
        return text.Length > width ? text[..width] : text.PadRight(width);
    }

    private static string Right(string? value, int width)
    {
        var text = Sanitize(value);
        return text.Length > width ? text[^width..] : text.PadLeft(width);
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace('\t', ' ')
            .Trim();
    }
}
