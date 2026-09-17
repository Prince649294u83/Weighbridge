using System.Globalization;
using System.IO.Compression;
using System.Text;
using WeighBridge.Core.Reporting;

namespace WeighBridge.Reporting.Services;

/// <summary>
/// Writes standard ECMA-376 / ISO/IEC 29500 OpenXML Spreadsheet (.xlsx) packages for weighbridge reports.
/// </summary>
public static class ExcelOpenXmlDocumentWriter
{
    public static async Task WriteAsync(ReportDocument doc, string filePath, CancellationToken cancellationToken = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"wb_export_{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create, true, Encoding.UTF8))
            {
                // 1. [Content_Types].xml
                AddEntry(zip, "[Content_Types].xml", GetContentTypesXml());

                // 2. _rels/.rels
                AddEntry(zip, "_rels/.rels", GetRootRelsXml());

                // 3. xl/_rels/workbook.xml.rels
                AddEntry(zip, "xl/_rels/workbook.xml.rels", GetWorkbookRelsXml());

                // 4. xl/workbook.xml
                AddEntry(zip, "xl/workbook.xml", GetWorkbookXml());

                // 5. xl/styles.xml
                AddEntry(zip, "xl/styles.xml", GetStylesXml());

                // 6. xl/worksheets/sheet1.xml
                AddEntry(zip, "xl/worksheets/sheet1.xml", GetWorksheetXml(doc));
            }

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Atomic move/copy to target file
            File.Copy(tempFile, filePath, true);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { /* best effort cleanup */ }
            }
        }

        await Task.CompletedTask;
    }

    private static void AddEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        writer.Write(content);
    }

    private static string GetContentTypesXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """;
    }

    private static string GetRootRelsXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """;
    }

    private static string GetWorkbookRelsXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """;
    }

    private static string GetWorkbookXml()
    {
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Weighments" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """;
    }

    private static string GetStylesXml()
    {
        // Styles:
        // 0: Normal
        // 1: Header style (Bold, Fill #2563EB or Light Slate #E2E8F0, Border)
        // 2: Bold summary
        // 3: Number integer (#,##0)
        // 4: Number decimal (#,##0.00)
        // 5: Date format (YYYY-MM-DD)
        return """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <numFmts count="2">
                <numFmt numFmtId="164" formatCode="#,##0"/>
                <numFmt numFmtId="165" formatCode="yyyy-mm-dd hh:mm"/>
              </numFmts>
              <fonts count="3">
                <font><sz val="10"/><name val="Segoe UI"/></font>
                <font><b/><sz val="10"/><name val="Segoe UI"/></font>
                <font><b/><sz val="14"/><name val="Segoe UI"/></font>
              </fonts>
              <fills count="3">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFE2E8F0"/></patternFill></fill>
              </fills>
              <borders count="2">
                <border><left/><right/><top/><bottom/><diagonal/></border>
                <border>
                  <left style="thin"><color rgb="FFCBD5E1"/></left>
                  <right style="thin"><color rgb="FFCBD5E1"/></right>
                  <top style="thin"><color rgb="FFCBD5E1"/></top>
                  <bottom style="thin"><color rgb="FFCBD5E1"/></bottom>
                  <diagonal/>
                </border>
              </borders>
              <cellStyleXfs count="1">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
              </cellStyleXfs>
              <cellXfs count="5">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1"/>
                <xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1"/>
                <xf numFmtId="0" fontId="1" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1"/>
                <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1"/>
                <xf numFmtId="164" fontId="1" fillId="2" borderId="1" xfId="0" applyNumberFormat="1" applyFont="1" applyFill="1" applyBorder="1"/>
              </cellXfs>
            </styleSheet>
            """;
    }

    private static string GetWorksheetXml(ReportDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.AppendLine("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

        // Column widths
        sb.AppendLine("  <cols>");
        sb.AppendLine("    <col min=\"1\" max=\"1\" width=\"8\" customWidth=\"1\"/>");   // Sr
        sb.AppendLine("    <col min=\"2\" max=\"2\" width=\"16\" customWidth=\"1\"/>");  // Slip
        sb.AppendLine("    <col min=\"3\" max=\"3\" width=\"18\" customWidth=\"1\"/>");  // Vehicle
        sb.AppendLine("    <col min=\"4\" max=\"4\" width=\"14\" customWidth=\"1\"/>");  // Type
        sb.AppendLine("    <col min=\"5\" max=\"5\" width=\"28\" customWidth=\"1\"/>");  // Party
        sb.AppendLine("    <col min=\"6\" max=\"6\" width=\"20\" customWidth=\"1\"/>");  // Material
        sb.AppendLine("    <col min=\"7\" max=\"7\" width=\"14\" customWidth=\"1\"/>");  // Charges 1
        sb.AppendLine("    <col min=\"8\" max=\"8\" width=\"14\" customWidth=\"1\"/>");  // Charges 2
        sb.AppendLine("    <col min=\"9\" max=\"9\" width=\"16\" customWidth=\"1\"/>");  // Total Charges
        sb.AppendLine("    <col min=\"10\" max=\"10\" width=\"14\" customWidth=\"1\"/>"); // Gross
        sb.AppendLine("    <col min=\"11\" max=\"11\" width=\"14\" customWidth=\"1\"/>"); // Tare
        sb.AppendLine("    <col min=\"12\" max=\"12\" width=\"14\" customWidth=\"1\"/>"); // Net
        sb.AppendLine("    <col min=\"13\" max=\"13\" width=\"20\" customWidth=\"1\"/>"); // Date
        sb.AppendLine("    <col min=\"14\" max=\"14\" width=\"14\" customWidth=\"1\"/>"); // Status
        sb.AppendLine("  </cols>");

        sb.AppendLine("  <sheetData>");

        var rowNum = 1;

        // Row 1: Company Title
        sb.AppendLine($"    <row r=\"{rowNum}\">");
        sb.AppendLine($"      <c r=\"A{rowNum}\" t=\"inlineStr\"><is><t>{EscapeXml(doc.CompanyName)} - {EscapeXml(doc.Title)}</t></is></c>");
        sb.AppendLine("    </row>");
        rowNum++;

        // Row 2: Filter Period
        sb.AppendLine($"    <row r=\"{rowNum}\">");
        sb.AppendLine($"      <c r=\"A{rowNum}\" t=\"inlineStr\"><is><t>Period: {doc.StartDateLocal:dd-MM-yyyy} to {doc.EndDateLocal:dd-MM-yyyy} | Generated: {DateTime.Now:dd-MM-yyyy HH:mm}</t></is></c>");
        sb.AppendLine("    </row>");
        rowNum++;

        // Row 3: Header Row
        sb.AppendLine($"    <row r=\"{rowNum}\">");
        string[] headers = ["Sr", "Slip No", "Vehicle No", "Type", "Party Name", "Material", "Charges 1st", "Charges 2nd", "Total Charges", "Gross (kg)", "Tare (kg)", "Net (kg)", "Date & Time", "Status"];
        for (var c = 0; c < headers.Length; c++)
        {
            var colLetter = GetColumnLetter(c + 1);
            sb.AppendLine($"      <c r=\"{colLetter}{rowNum}\" s=\"1\" t=\"inlineStr\"><is><t>{EscapeXml(headers[c])}</t></is></c>");
        }
        sb.AppendLine("    </row>");
        rowNum++;

        // Data Rows
        foreach (var r in doc.Rows)
        {
            sb.AppendLine($"    <row r=\"{rowNum}\">");
            sb.AppendLine($"      <c r=\"A{rowNum}\" s=\"3\"><v>{r.SerialNumber}</v></c>");
            sb.AppendLine($"      <c r=\"B{rowNum}\" s=\"0\" t=\"inlineStr\"><is><t>{EscapeXml(r.SlipNumber)}</t></is></c>");
            sb.AppendLine($"      <c r=\"C{rowNum}\" s=\"0\" t=\"inlineStr\"><is><t>{EscapeXml(r.VehicleNumber)}</t></is></c>");
            sb.AppendLine($"      <c r=\"D{rowNum}\" s=\"0\" t=\"inlineStr\"><is><t>{EscapeXml(r.VehicleTypeName)}</t></is></c>");
            sb.AppendLine($"      <c r=\"E{rowNum}\" s=\"0\" t=\"inlineStr\"><is><t>{EscapeXml(r.PartyName)}</t></is></c>");
            sb.AppendLine($"      <c r=\"F{rowNum}\" s=\"0\" t=\"inlineStr\"><is><t>{EscapeXml(r.MaterialName)}</t></is></c>");
            sb.AppendLine($"      <c r=\"G{rowNum}\" s=\"3\"><v>{r.Charges1.ToString(CultureInfo.InvariantCulture)}</v></c>");
            sb.AppendLine($"      <c r=\"H{rowNum}\" s=\"3\"><v>{r.Charges2.ToString(CultureInfo.InvariantCulture)}</v></c>");
            sb.AppendLine($"      <c r=\"I{rowNum}\" s=\"3\"><v>{r.TotalCharges.ToString(CultureInfo.InvariantCulture)}</v></c>");
            sb.AppendLine($"      <c r=\"J{rowNum}\" s=\"3\"><v>{r.GrossWeightKg.ToString(CultureInfo.InvariantCulture)}</v></c>");
            sb.AppendLine($"      <c r=\"K{rowNum}\" s=\"3\"><v>{r.TareWeightKg.ToString(CultureInfo.InvariantCulture)}</v></c>");
            sb.AppendLine($"      <c r=\"L{rowNum}\" s=\"3\"><v>{r.NetWeightKg.ToString(CultureInfo.InvariantCulture)}</v></c>");
            sb.AppendLine($"      <c r=\"M{rowNum}\" s=\"0\" t=\"inlineStr\"><is><t>{EscapeXml(r.GrossCapturedAtLocal?.ToString("yyyy-MM-dd HH:mm:ss") ?? "")}</t></is></c>");
            sb.AppendLine($"      <c r=\"N{rowNum}\" s=\"0\" t=\"inlineStr\"><is><t>{EscapeXml(r.Status)}</t></is></c>");
            sb.AppendLine("    </row>");
            rowNum++;
        }

        // Totals Row
        sb.AppendLine($"    <row r=\"{rowNum}\">");
        sb.AppendLine($"      <c r=\"A{rowNum}\" s=\"2\" t=\"inlineStr\"><is><t>Total Records</t></is></c>");
        sb.AppendLine($"      <c r=\"B{rowNum}\" s=\"4\"><v>{doc.TotalRecordCount}</v></c>");
        sb.AppendLine($"      <c r=\"C{rowNum}\" s=\"2\"/>");
        sb.AppendLine($"      <c r=\"D{rowNum}\" s=\"2\"/>");
        sb.AppendLine($"      <c r=\"E{rowNum}\" s=\"2\"/>");
        sb.AppendLine($"      <c r=\"F{rowNum}\" s=\"2\"/>");
        sb.AppendLine($"      <c r=\"G{rowNum}\" s=\"2\"/>");
        sb.AppendLine($"      <c r=\"H{rowNum}\" s=\"2\"/>");
        sb.AppendLine($"      <c r=\"I{rowNum}\" s=\"4\"><v>{doc.TotalCharges.ToString(CultureInfo.InvariantCulture)}</v></c>");
        sb.AppendLine($"      <c r=\"J{rowNum}\" s=\"2\"/>");
        sb.AppendLine($"      <c r=\"K{rowNum}\" s=\"2\"/>");
        sb.AppendLine($"      <c r=\"L{rowNum}\" s=\"4\"><v>{doc.TotalNetWeightKg.ToString(CultureInfo.InvariantCulture)}</v></c>");
        sb.AppendLine($"      <c r=\"M{rowNum}\" s=\"2\"/>");
        sb.AppendLine($"      <c r=\"N{rowNum}\" s=\"2\"/>");
        sb.AppendLine("    </row>");

        sb.AppendLine("  </sheetData>");
        sb.AppendLine("</worksheet>");

        return sb.ToString();
    }

    private static string GetColumnLetter(int colIndex)
    {
        var dividend = colIndex;
        var columnName = string.Empty;
        while (dividend > 0)
        {
            var modulo = (dividend - 1) % 26;
            columnName = Convert.ToChar(65 + modulo) + columnName;
            dividend = (dividend - modulo) / 26;
        }
        return columnName;
    }

    private static string EscapeXml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
}
