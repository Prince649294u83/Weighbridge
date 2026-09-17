using System.Globalization;
using System.Text;
using WeighBridge.Core.Reporting;

namespace WeighBridge.Reporting.Services;

/// <summary>
/// Writes standard ISO 32000-1 / PDF 1.4 compliant binary PDF documents for weighbridge reports
/// with automatic pagination, table grid rendering, headers, footers, and summary totals.
/// </summary>
public static class PdfReportDocumentWriter
{
    private const double PageWidth = 841.89; // A4 Landscape width in points
    private const double PageHeight = 595.28; // A4 Landscape height in points
    private const double MarginLeft = 36.0; // 0.5 inch margin
    private const double MarginRight = 36.0;
    private const double MarginTop = 36.0;
    private const double MarginBottom = 36.0;

    private const double UsableWidth = PageWidth - MarginLeft - MarginRight; // 769.89 pt
    private const double RowHeight = 16.0;
    private const double HeaderRowHeight = 20.0;
    private const double TitleBlockHeight = 60.0;
    private const double FooterHeight = 20.0;

    // Column widths in points (total = 769.89 pt)
    private static readonly (string Header, double Width, bool AlignRight)[] Columns =
    [
        ("Sr", 30, false),
        ("Slip No", 65, false),
        ("Vehicle No", 80, false),
        ("Type", 55, false),
        ("Party Name", 125, false),
        ("Material", 90, false),
        ("Charges", 55, true),
        ("Gross (kg)", 55, true),
        ("Tare (kg)", 55, true),
        ("Net (kg)", 55, true),
        ("Date", 65, false),
        ("Status", 40, false)
    ];

    public static byte[] GeneratePdf(ReportDocument doc)
    {
        var rows = doc.Rows;
        var totalRows = rows.Count;

        // Calculate rows per page
        var usableHeightPage1 = PageHeight - MarginTop - TitleBlockHeight - HeaderRowHeight - FooterHeight - MarginBottom;
        var maxRowsPage1 = Math.Max(1, (int)(usableHeightPage1 / RowHeight));

        var usableHeightSubsequent = PageHeight - MarginTop - HeaderRowHeight - FooterHeight - MarginBottom;
        var maxRowsSubsequent = Math.Max(1, (int)(usableHeightSubsequent / RowHeight));

        var pages = new List<List<ReportDocumentRow>>();
        if (totalRows == 0)
        {
            pages.Add([]);
        }
        else
        {
            var currentIndex = 0;
            // Page 1
            var page1Count = Math.Min(totalRows, maxRowsPage1);
            pages.Add(rows.Take(page1Count).ToList());
            currentIndex += page1Count;

            // Subsequent pages
            while (currentIndex < totalRows)
            {
                var count = Math.Min(totalRows - currentIndex, maxRowsSubsequent);
                pages.Add(rows.Skip(currentIndex).Take(count).ToList());
                currentIndex += count;
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

        // 1. Header
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

        // Obj 4: Font Helvetica
        WriteObject(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        // Obj 5: Font Helvetica-Bold
        WriteObject(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        // Build Pages and Content Streams
        for (var pageIdx = 0; pageIdx < totalPages; pageIdx++)
        {
            var pageRows = pages[pageIdx];
            var isFirstPage = pageIdx == 0;
            var isLastPage = pageIdx == totalPages - 1;
            var pageNum = pageIdx + 1;

            var streamContent = GeneratePageStreamContent(doc, pageRows, isFirstPage, isLastPage, pageNum, totalPages);
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
        bool isFirstPage,
        bool isLastPage,
        int pageNum,
        int totalPages)
    {
        var cb = new StringBuilder();

        var currentY = PageHeight - MarginTop;

        // 1. Title Block (Page 1 only)
        if (isFirstPage)
        {
            // Company Name
            cb.AppendLine("BT /F2 14 Tf 0.1 0.1 0.1 rg");
            var company = EscapePdfString(doc.CompanyName);
            cb.AppendLine($"{MarginLeft:F2} {currentY - 14:F2} Td ({company}) Tj ET");

            // Report Title
            cb.AppendLine("BT /F2 11 Tf 0.2 0.2 0.2 rg");
            var title = EscapePdfString(doc.Title);
            cb.AppendLine($"{MarginLeft:F2} {currentY - 30:F2} Td ({title}) Tj ET");

            // Metadata: Period & Generated Time
            cb.AppendLine("BT /F1 8.5 Tf 0.4 0.4 0.4 rg");
            var period = $"Period: {doc.StartDateLocal:dd-MM-yyyy} to {doc.EndDateLocal:dd-MM-yyyy}    |    Generated: {DateTime.Now:dd-MM-yyyy HH:mm}";
            cb.AppendLine($"{MarginLeft:F2} {currentY - 44:F2} Td ({EscapePdfString(period)}) Tj ET");

            // Separator line
            cb.AppendLine("0.8 0.8 0.8 RG 0.75 w");
            cb.AppendLine($"{MarginLeft:F2} {currentY - 52:F2} m {PageWidth - MarginRight:F2} {currentY - 52:F2} l S");

            currentY -= TitleBlockHeight;
        }

        // 2. Table Column Headers
        var tableTopY = currentY;
        var tableBottomY = currentY - HeaderRowHeight - (pageRows.Count * RowHeight) - (isLastPage ? RowHeight : 0);

        // Header Background
        cb.AppendLine("0.93 0.94 0.96 rg"); // Light slate #ECEEF2
        cb.AppendLine($"{MarginLeft:F2} {currentY - HeaderRowHeight:F2} {UsableWidth:F2} {HeaderRowHeight:F2} re f");

        // Header Text
        cb.AppendLine("0.1 0.1 0.1 rg");
        var curX = MarginLeft;
        foreach (var (header, width, alignRight) in Columns)
        {
            cb.AppendLine("BT /F2 8.5 Tf");
            var textX = alignRight ? curX + width - 4.0 : curX + 4.0;
            if (alignRight)
            {
                var estLen = header.Length * 4.5;
                textX = curX + width - estLen - 4.0;
            }
            cb.AppendLine($"{textX:F2} {currentY - 14:F2} Td ({EscapePdfString(header)}) Tj ET");
            curX += width;
        }

        currentY -= HeaderRowHeight;

        // 3. Table Rows
        var rowIdx = 0;
        foreach (var r in pageRows)
        {
            var rowY = currentY - RowHeight;

            // Alternating row background
            if (rowIdx % 2 == 1)
            {
                cb.AppendLine("0.98 0.98 0.99 rg");
                cb.AppendLine($"{MarginLeft:F2} {rowY:F2} {UsableWidth:F2} {RowHeight:F2} re f");
            }

            // Cell Data
            string[] cellValues =
            [
                r.SerialNumber.ToString(CultureInfo.InvariantCulture),
                r.SlipNumber ?? string.Empty,
                r.VehicleNumber ?? string.Empty,
                r.VehicleTypeName ?? string.Empty,
                r.PartyName ?? string.Empty,
                r.MaterialName ?? string.Empty,
                r.TotalCharges > 0 ? r.TotalCharges.ToString("N0", CultureInfo.InvariantCulture) : "0",
                r.GrossWeightKg.ToString("N0", CultureInfo.InvariantCulture),
                r.TareWeightKg.ToString("N0", CultureInfo.InvariantCulture),
                r.NetWeightKg.ToString("N0", CultureInfo.InvariantCulture),
                r.GrossCapturedAtLocal?.ToString("dd-MM-yyyy") ?? string.Empty,
                r.Status ?? string.Empty
            ];

            curX = MarginLeft;
            for (var c = 0; c < Columns.Length; c++)
            {
                var (hdr, width, alignRight) = Columns[c];
                var val = cellValues[c];

                var maxChars = (int)(width / 4.8);
                if (val.Length > maxChars && maxChars > 3)
                {
                    val = val[..(maxChars - 2)] + "..";
                }

                cb.AppendLine("BT /F1 8 Tf 0.15 0.15 0.15 rg");
                var textX = curX + 4.0;
                if (alignRight)
                {
                    var estLen = val.Length * 4.2;
                    textX = Math.Max(curX + 2.0, curX + width - estLen - 4.0);
                }
                cb.AppendLine($"{textX:F2} {rowY + 4.5:F2} Td ({EscapePdfString(val)}) Tj ET");
                curX += width;
            }

            // Row Bottom Border
            cb.AppendLine("0.9 0.9 0.9 RG 0.5 w");
            cb.AppendLine($"{MarginLeft:F2} {rowY:F2} m {PageWidth - MarginRight:F2} {rowY:F2} l S");

            currentY -= RowHeight;
            rowIdx++;
        }

        // 4. Totals Row (Last Page only)
        if (isLastPage)
        {
            var rowY = currentY - RowHeight;
            cb.AppendLine("0.90 0.92 0.96 rg");
            cb.AppendLine($"{MarginLeft:F2} {rowY:F2} {UsableWidth:F2} {RowHeight:F2} re f");

            // Total label
            cb.AppendLine("BT /F2 8.5 Tf 0.1 0.1 0.1 rg");
            var totalLabel = $"Total ({doc.TotalRecordCount} Records)";
            cb.AppendLine($"{MarginLeft + 4.0:F2} {rowY + 4.5:F2} Td ({EscapePdfString(totalLabel)}) Tj ET");

            // Total Charges
            var chargesColX = MarginLeft + Columns.Take(6).Sum(c => c.Width);
            var chargesWidth = Columns[6].Width;
            var chargesStr = doc.TotalCharges.ToString("N0", CultureInfo.InvariantCulture);
            var chargesEstLen = chargesStr.Length * 4.5;
            cb.AppendLine("BT /F2 8.5 Tf 0.1 0.1 0.1 rg");
            cb.AppendLine($"{chargesColX + chargesWidth - chargesEstLen - 4.0:F2} {rowY + 4.5:F2} Td ({EscapePdfString(chargesStr)}) Tj ET");

            // Total Net Weight
            var netColX = MarginLeft + Columns.Take(9).Sum(c => c.Width);
            var netWidth = Columns[9].Width;
            var netStr = doc.TotalNetWeightKg.ToString("N0", CultureInfo.InvariantCulture);
            var netEstLen = netStr.Length * 4.5;
            cb.AppendLine("BT /F2 8.5 Tf 0.1 0.1 0.1 rg");
            cb.AppendLine($"{netColX + netWidth - netEstLen - 4.0:F2} {rowY + 4.5:F2} Td ({EscapePdfString(netStr)}) Tj ET");

            // Total row border
            cb.AppendLine("0.6 0.6 0.6 RG 0.75 w");
            cb.AppendLine($"{MarginLeft:F2} {rowY:F2} m {PageWidth - MarginRight:F2} {rowY:F2} l S");

            currentY -= RowHeight;
        }

        // Table Outer & Vertical Grid Lines
        cb.AppendLine("0.8 0.8 0.8 RG 0.5 w");
        // Outer border
        cb.AppendLine($"{MarginLeft:F2} {tableBottomY:F2} {UsableWidth:F2} {tableTopY - tableBottomY:F2} re S");

        // Vertical dividers
        curX = MarginLeft;
        for (var i = 0; i < Columns.Length - 1; i++)
        {
            curX += Columns[i].Width;
            cb.AppendLine($"{curX:F2} {tableBottomY:F2} m {curX:F2} {tableTopY:F2} l S");
        }

        // 5. Page Footer
        cb.AppendLine("0.7 0.7 0.7 RG 0.5 w");
        cb.AppendLine($"{MarginLeft:F2} {MarginBottom + 12:F2} m {PageWidth - MarginRight:F2} {MarginBottom + 12:F2} l S");

        cb.AppendLine("BT /F1 8 Tf 0.45 0.45 0.45 rg");
        var appFooter = "WeighBridge Modern - Authoritative Report Output";
        cb.AppendLine($"{MarginLeft:F2} {MarginBottom + 2:F2} Td ({EscapePdfString(appFooter)}) Tj ET");

        var pageStr = $"Page {pageNum} of {totalPages}";
        cb.AppendLine("BT /F1 8 Tf 0.45 0.45 0.45 rg");
        cb.AppendLine($"{PageWidth - MarginRight - 50:F2} {MarginBottom + 2:F2} Td ({EscapePdfString(pageStr)}) Tj ET");

        return cb.ToString();
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
                    if (c < 32 || c > 126)
                    {
                        sb.Append(c switch
                        {
                            '₹' => "Rs.",
                            '—' => "-",
                            '–' => "-",
                            '\u201C' => "\"", // Left double quotation mark
                            '\u201D' => "\"", // Right double quotation mark
                            '\u2018' => "'",  // Left single quotation mark
                            '\u2019' => "'",  // Right single quotation mark
                            _ => ' '
                        });
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
