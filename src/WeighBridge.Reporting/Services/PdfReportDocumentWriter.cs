using System.Globalization;
using System.Text;
using WeighBridge.Core.Reporting;

namespace WeighBridge.Reporting.Services;

/// <summary>
/// Writes standard ISO 32000-1 / PDF 1.4 compliant binary PDF documents for weighbridge reports
/// in a classic monospaced text layout matching legacy dot-matrix / typewriter styling.
/// </summary>
public static class PdfReportDocumentWriter
{
    private const double PageWidth = 841.89; // A4 Landscape width in points
    private const double PageHeight = 595.28; // A4 Landscape height in points
    private const double MarginTop = 40.0;
    private const double MarginBottom = 40.0;
    private const double FontSize = 9.0;
    private const double LineHeight = 13.0;
    private const double CharWidth = 5.4; // In Courier Type 1, char width is 0.6 * FontSize (0.6 * 9.0 = 5.4 pt)
    private const int ReportWidthChars = 131;

    private static readonly double MarginLeft = Math.Max(20.0, (PageWidth - (ReportWidthChars * CharWidth)) / 2.0);

    public const string HeaderLine = "S.No   Vehicle No.    Vehicle Type   Party Name    Material        Chg1 Chg2  G Wt.  T Wt.  N Wt.  GWt Date/Time     TWt Date/Time ";
    public static readonly string DividerLine = new('-', ReportWidthChars);

    public static byte[] GeneratePdf(ReportDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var rows = doc.Rows;
        var totalRows = rows.Count;

        var companyLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(doc.CompanyName)) companyLines.Add(doc.CompanyName.Trim());
        if (!string.IsNullOrWhiteSpace(doc.AddressLine1)) companyLines.Add(doc.AddressLine1.Trim());
        if (!string.IsNullOrWhiteSpace(doc.AddressLine2)) companyLines.Add(doc.AddressLine2.Trim());
        if (companyLines.Count == 0) companyLines.Add(string.Empty);

        // Calculate available line capacity per page
        var usableHeight = PageHeight - MarginTop - MarginBottom;
        var maxLinesPerPage = Math.Max(10, (int)(usableHeight / LineHeight));

        var page1HeaderLineCount = companyLines.Count + 1 + 1 + 1 + 1 + 1 + 1; // company + blank + subheader + blank + div + hdr + div
        var footerLineCount = 1 + 3; // div + 3 summary lines

        var usableLinesPage1Single = maxLinesPerPage - page1HeaderLineCount - footerLineCount;
        var usableLinesPage1Multi = maxLinesPerPage - page1HeaderLineCount;
        var usableLinesSubsequentMulti = maxLinesPerPage - 2; // hdr + div
        var usableLinesSubsequentLast = maxLinesPerPage - 2 - footerLineCount;

        var pages = new List<List<ReportDocumentRow>>();
        if (totalRows <= usableLinesPage1Single)
        {
            pages.Add(rows.ToList());
        }
        else
        {
            pages.Add(rows.Take(usableLinesPage1Multi).ToList());
            var currentIndex = usableLinesPage1Multi;

            while (currentIndex < totalRows)
            {
                var remaining = totalRows - currentIndex;
                if (remaining <= usableLinesSubsequentLast)
                {
                    pages.Add(rows.Skip(currentIndex).Take(remaining).ToList());
                    break;
                }

                pages.Add(rows.Skip(currentIndex).Take(usableLinesSubsequentMulti).ToList());
                currentIndex += usableLinesSubsequentMulti;
            }
        }

        var totalPages = pages.Count;
        var pageObjectIndices = new List<int>();
        var contentObjectIndices = new List<int>();

        var nextObjId = 6;
        for (var p = 0; p < totalPages; p++)
        {
            pageObjectIndices.Add(nextObjId++);
            contentObjectIndices.Add(nextObjId++);
        }

        var offsets = new List<long>();

        // 1. PDF Header
        var headerBytes = Encoding.ASCII.GetBytes("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        using var ms = new MemoryStream();
        ms.Write(headerBytes, 0, headerBytes.Length);

        void WriteObject(int id, string content)
        {
            offsets.Add(ms.Position);
            var objStr = $"{id} 0 obj\n{content}\nendobj\n";
            var bytes = Encoding.ASCII.GetBytes(objStr);
            ms.Write(bytes, 0, bytes.Length);
        }

        // Obj 1: Catalog
        WriteObject(1, "<< /Type /Catalog /Pages 3 0 R >>");

        // Obj 2: Outlines
        WriteObject(2, "<< /Type /Outlines /Count 0 >>");

        // Obj 3: Pages
        var kidsStr = string.Join(" ", pageObjectIndices.Select(i => $"{i} 0 R"));
        WriteObject(3, $"<< /Type /Pages /Kids [{kidsStr}] /Count {totalPages} >>");

        // Obj 4: Font Courier
        WriteObject(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>");

        // Obj 5: Font Courier-Bold
        WriteObject(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>");

        // Build Pages and Content Streams
        for (var pageIdx = 0; pageIdx < totalPages; pageIdx++)
        {
            var pageRows = pages[pageIdx];
            var isFirstPage = pageIdx == 0;
            var isLastPage = pageIdx == totalPages - 1;

            var streamContent = GeneratePageStreamContent(doc, pageRows, companyLines, isFirstPage, isLastPage);
            var streamBytes = Encoding.ASCII.GetBytes(streamContent);

            // Page Object
            var pageObj = $"<< /Type /Page /Parent 3 0 R /MediaBox [0 0 {PageWidth:F2} {PageHeight:F2}] /Contents {contentObjectIndices[pageIdx]} 0 R /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> >>";
            WriteObject(pageObjectIndices[pageIdx], pageObj);

            // Content Stream Object
            offsets.Add(ms.Position);
            var streamHeader = $"{contentObjectIndices[pageIdx]} 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n";
            var headerB = Encoding.ASCII.GetBytes(streamHeader);
            ms.Write(headerB, 0, headerB.Length);
            ms.Write(streamBytes, 0, streamBytes.Length);
            var streamFooter = "\nendstream\nendobj\n";
            var footerB = Encoding.ASCII.GetBytes(streamFooter);
            ms.Write(footerB, 0, footerB.Length);
        }

        // Obj Info: Document metadata
        var infoObjId = nextObjId;
        var infoObj = $"<< /Title ({EscapePdfString(doc.Title)}) /Author (WeighBridge Modern) /Creator (WeighBridge Modern) /CreationDate (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z) >>";
        WriteObject(infoObjId, infoObj);

        // Cross-reference table
        var xrefOffset = ms.Position;
        var totalObjects = infoObjId;

        var xrefSb = new StringBuilder();
        xrefSb.AppendLine("xref");
        xrefSb.AppendLine($"0 {totalObjects + 1}");
        xrefSb.AppendLine("0000000000 65535 f ");
        for (var i = 0; i < totalObjects; i++)
        {
            xrefSb.AppendLine($"{offsets[i]:D10} 00000 n ");
        }

        xrefSb.AppendLine("trailer");
        xrefSb.AppendLine($"<< /Size {totalObjects + 1} /Root 1 0 R /Info {infoObjId} 0 R >>");
        xrefSb.AppendLine("startxref");
        xrefSb.AppendLine($"{xrefOffset}");
        xrefSb.AppendLine("%%EOF");

        var xrefBytes = Encoding.ASCII.GetBytes(xrefSb.ToString());
        ms.Write(xrefBytes, 0, xrefBytes.Length);

        return ms.ToArray();
    }

    private static string GeneratePageStreamContent(
        ReportDocument doc,
        List<ReportDocumentRow> pageRows,
        List<string> companyLines,
        bool isFirstPage,
        bool isLastPage)
    {
        var lines = new List<string>();

        if (isFirstPage)
        {
            foreach (var c in companyLines)
            {
                lines.Add(CenterText(c, ReportWidthChars));
            }

            lines.Add(string.Empty);
            lines.Add($"Report From Date - {doc.StartDateLocal:M/d/yyyy} To Date - {doc.EndDateLocal:M/d/yyyy}");
            lines.Add(string.Empty);
            lines.Add(DividerLine);
            lines.Add(HeaderLine);
            lines.Add(DividerLine);
        }
        else
        {
            lines.Add(HeaderLine);
            lines.Add(DividerLine);
        }

        foreach (var r in pageRows)
        {
            lines.Add(FormatDataRow(r));
        }

        if (isLastPage)
        {
            lines.Add(DividerLine);
            lines.Add("Total No of Records".PadRight(20) + ": " + doc.TotalRecordCount);
            var netWeightStr = doc.TotalNetWeightKg.ToString("0", CultureInfo.InvariantCulture);
            lines.Add("Total Net Weight".PadRight(20) + ": " + netWeightStr + " Kg.");
            var chargesStr = doc.TotalCharges.ToString("0", CultureInfo.InvariantCulture);
            lines.Add("Total Charges".PadRight(20) + ": " + chargesStr + " /-");
        }

        var cb = new StringBuilder();
        var currentY = PageHeight - MarginTop - LineHeight;

        foreach (var line in lines)
        {
            if (!string.IsNullOrEmpty(line))
            {
                var escaped = EscapePdfString(line);
                cb.AppendLine($"BT /F1 {FontSize:F1} Tf 0 0 0 rg");
                cb.AppendLine($"{MarginLeft:F2} {currentY:F2} Td ({escaped}) Tj ET");
            }

            currentY -= LineHeight;
        }

        return cb.ToString();
    }

    public static string FormatDataRow(ReportDocumentRow r)
    {
        var veh = TruncateOrPad(r.VehicleNumber, 11, false);
        var type = TruncateOrPad(r.VehicleTypeName, 12, false);
        var party = TruncateOrPad(r.PartyName, 10, false);
        var mat = TruncateOrPad(r.MaterialName, 8, false);

        var chg1Str = r.Charges1 > 0 ? r.Charges1.ToString("0", CultureInfo.InvariantCulture) : "0";
        var chg2Str = r.Charges2 > 0 ? r.Charges2.ToString("0", CultureInfo.InvariantCulture) : "0";
        var chg1 = TruncateOrPad(chg1Str, 4, true);
        var chg2 = TruncateOrPad(chg2Str, 4, true);

        var gwtStr = r.GrossWeightKg > 0 ? r.GrossWeightKg.ToString("0", CultureInfo.InvariantCulture) : "0";
        var twtStr = r.TareWeightKg > 0 ? r.TareWeightKg.ToString("0", CultureInfo.InvariantCulture) : "0";
        var nwtStr = r.NetWeightKg > 0 ? r.NetWeightKg.ToString("0", CultureInfo.InvariantCulture) : "0";

        var gwt = TruncateOrPad(gwtStr, 5, true);
        var twt = TruncateOrPad(twtStr, 5, true);
        var nwt = TruncateOrPad(nwtStr, 5, true);

        var gDateStr = r.GrossCapturedAtLocal?.ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
        var tDateStr = r.TareCapturedAtLocal?.ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;

        var gDate = TruncateOrPad(gDateStr, 14, false);
        var tDate = TruncateOrPad(tDateStr, 14, false);

        var sno = TruncateOrPad(r.SerialNumber.ToString(CultureInfo.InvariantCulture), 4, true);

        return $"{sno}   {veh}    {type}   {party}    {mat}        {chg1} {chg2}  {gwt}  {twt}  {nwt}  {gDate}    {tDate}";
    }

    public static string CenterText(string? text, int width)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        if (trimmed.Length >= width) return trimmed[..width];
        var leftPadding = (width - trimmed.Length) / 2;
        return new string(' ', leftPadding) + trimmed;
    }

    private static string TruncateOrPad(string? text, int width, bool alignRight)
    {
        var s = text ?? string.Empty;
        if (s.Length > width)
        {
            s = s[..width];
        }
        return alignRight ? s.PadLeft(width) : s.PadRight(width);
    }

    private static string EscapePdfString(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var sb = new StringBuilder(text.Length + 10);
        foreach (var c in text)
        {
            switch (c)
            {
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\r': break;
                case '\n': sb.Append(' '); break;
                default:
                    if (c >= 32 && c <= 126)
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append(c switch
                        {
                            '₹' => "Rs.",
                            '—' => "-",
                            '–' => "-",
                            '\u201C' => "\"",
                            '\u201D' => "\"",
                            '\u2018' => "'",
                            '\u2019' => "'",
                            _ => ' '
                        });
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
