using System.IO;
using System.Text;
using WeighBridge.Core.Printing;
using WeighBridge.Printing.Template.Ast;

namespace WeighBridge.Printing.Template;

/// <summary>
/// Authoritative AST-based template engine for parsing, validating, and rendering
/// weighment slip templates according to legacy parity specifications.
/// </summary>
public sealed class SlipTemplateEngine : ITemplateEngine
{
    public const int MaxTemplateBytes = 64 * 1024; // 64 KB
    public const int MaxLineLength = 1024;
    public const int MaxTokenLength = 64;

    private static readonly Dictionary<string, string> CanonicalTokenMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Name"] = "CompanyName",
        ["Address1"] = "AddressLine1",
        ["Address2"] = "AddressLine2",
        ["date"] = "TransactionDate",
        ["ticket"] = "SlipNumber",
        ["party"] = "PartyName",
        ["vehicle"] = "VehicleNumber",
        ["vtype"] = "VehicleTypeName",
        ["item"] = "MaterialName",
        ["material"] = "MaterialName",
        ["charges"] = "TotalCharges",
        ["gweight"] = "GrossWeightKg",
        ["gdate"] = "GrossDate",
        ["gtime"] = "GrossTime",
        ["tweight"] = "TareWeightKg",
        ["tdate"] = "TareDate",
        ["ttime"] = "TareTime",
        ["nweight"] = "NetWeightKg",
        ["bags"] = "NumberOfBags",
        ["field2"] = "NumberOfBags",
        ["emptybagwt"] = "BagWeightKg",
        ["field3"] = "BagWeightKg",
        ["totalbagweight"] = "TotalBagWeightKg",
        ["acualwt"] = "ActualWeightKg",
        ["field4"] = "ActualWeightKg",
        ["gatepass"] = "GatePassNumber",
        ["field1"] = "CustomField1",
        ["field2_custom"] = "CustomField2",
        ["field3_custom"] = "CustomField3",
        ["field4_custom"] = "CustomField4",
        ["username"] = "OperatorDisplayName",
        ["SlipType"] = "SlipType",
        ["remarks"] = "Remarks"
    };

    private static readonly HashSet<string> StandaloneDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "Start",
        "-",
        "Cut"
    };

    private static readonly HashSet<string> ScopedFormattingDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bold",
        "Header",
        "Nameset",
        "TNameset",
        "TNameset1"
    };

    /// <inheritdoc />
    public TemplateValidationResult Validate(string templateContent, PrinterProfile? profile = null)
    {
        if (string.IsNullOrEmpty(templateContent))
        {
            return TemplateValidationResult.Failure(TemplateErrorCategory.MalformedDirective, "Template content is empty.", 1, 1);
        }

        var errors = new List<TemplateValidationError>();

        // Pre-parse limits check
        if (Encoding.UTF8.GetByteCount(templateContent) > MaxTemplateBytes)
        {
            errors.Add(new TemplateValidationError(
                TemplateErrorCategory.LimitExceeded,
                $"Template exceeds maximum size limit of {MaxTemplateBytes} bytes.",
                1, 1));
            return TemplateValidationResult.Failure(errors);
        }

        using var reader = new StringReader(templateContent);
        string? line;
        int lineNum = 0;
        var openTags = new Stack<(string Tag, int Line, int Col)>();

        while ((line = reader.ReadLine()) is not null)
        {
            lineNum++;
            if (line.Length > MaxLineLength)
            {
                errors.Add(new TemplateValidationError(
                    TemplateErrorCategory.LimitExceeded,
                    $"Line {lineNum} exceeds maximum length limit of {MaxLineLength} characters.",
                    lineNum, 1));
            }

            int index = 0;
            while (index < line.Length)
            {
                int openBracket = line.IndexOf('<', index);
                if (openBracket == -1) break;

                int nextOpenBracket = line.IndexOf('<', openBracket + 1);
                int closeBracket = line.IndexOf('>', openBracket);

                if (closeBracket == -1 || (nextOpenBracket != -1 && nextOpenBracket < closeBracket))
                {
                    errors.Add(new TemplateValidationError(
                        TemplateErrorCategory.UnclosedToken,
                        $"Unclosed tag '<' at line {lineNum}, column {openBracket + 1}.",
                        lineNum, openBracket + 1));
                    index = nextOpenBracket != -1 ? nextOpenBracket : line.Length;
                    continue;
                }

                string tagContent = line.Substring(openBracket + 1, closeBracket - openBracket - 1).Trim();
                int tagCol = openBracket + 1;
                index = closeBracket + 1;

                if (tagContent.Length > MaxTokenLength)
                {
                    errors.Add(new TemplateValidationError(
                        TemplateErrorCategory.LimitExceeded,
                        $"Tag '{tagContent}' exceeds maximum token length of {MaxTokenLength} characters.",
                        lineNum, tagCol));
                    continue;
                }

                if (tagContent.StartsWith('/'))
                {
                    // Closing tag
                    string closingTagName = tagContent[1..].Trim();
                    if (openTags.Count == 0 || !string.Equals(openTags.Peek().Tag, closingTagName, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(new TemplateValidationError(
                            TemplateErrorCategory.InvalidNesting,
                            $"Unexpected closing tag '</{closingTagName}>' without matching opening tag at line {lineNum}, column {tagCol}.",
                            lineNum, tagCol));
                    }
                    else
                    {
                        openTags.Pop();
                    }
                    continue;
                }

                // Check standalone directives
                if (StandaloneDirectives.Contains(tagContent))
                {
                    if (string.Equals(tagContent, "Cut", StringComparison.OrdinalIgnoreCase) && profile is { OutputMode: PrinterOutputMode.Gdi, SupportsCut: false })
                    {
                        errors.Add(new TemplateValidationError(
                            TemplateErrorCategory.CapabilityMismatch,
                            $"Directive '<Cut>' is unsupported on GDI printer profile '{profile.PrinterName}'.",
                            lineNum, tagCol));
                    }
                    continue;
                }

                // Check scoped formatting directives
                if (ScopedFormattingDirectives.Contains(tagContent))
                {
                    openTags.Push((tagContent, lineNum, tagCol));
                    continue;
                }

                // Check token whitelist
                if (!CanonicalTokenMap.ContainsKey(tagContent))
                {
                    errors.Add(new TemplateValidationError(
                        TemplateErrorCategory.UnknownToken,
                        $"Unknown template token or directive '<{tagContent}>' at line {lineNum}, column {tagCol}.",
                        lineNum, tagCol));
                }
            }
        }

        while (openTags.Count > 0)
        {
            var unclosed = openTags.Pop();
            errors.Add(new TemplateValidationError(
                TemplateErrorCategory.UnclosedToken,
                $"Unclosed formatting directive '<{unclosed.Tag}>' from line {unclosed.Line}, column {unclosed.Col}.",
                unclosed.Line, unclosed.Col));
        }

        return errors.Count == 0
            ? TemplateValidationResult.Success()
            : TemplateValidationResult.Failure(errors);
    }

    /// <inheritdoc />
    public TemplateDocument Parse(string templateContent, bool strictValidation = false)
    {
        if (strictValidation)
        {
            var validation = Validate(templateContent);
            if (!validation.IsValid)
            {
                throw new TemplateParseException("Template validation failed in strict mode.", validation.Errors);
            }
        }
        else
        {
            // Pre-parse limits check even in non-strict mode
            if (string.IsNullOrEmpty(templateContent))
            {
                return new TemplateDocument([]);
            }
            if (Encoding.UTF8.GetByteCount(templateContent) > MaxTemplateBytes)
            {
                throw new TemplateParseException(TemplateErrorCategory.LimitExceeded, $"Template exceeds size limit of {MaxTemplateBytes} bytes.");
            }
        }

        var nodes = new List<TemplateNode>();
        int currentIndex = 0;

        while (currentIndex < templateContent.Length)
        {
            int openBracket = templateContent.IndexOf('<', currentIndex);
            if (openBracket == -1)
            {
                nodes.Add(new TextNode(templateContent[currentIndex..]));
                break;
            }

            if (openBracket > currentIndex)
            {
                nodes.Add(new TextNode(templateContent[currentIndex..openBracket]));
            }

            int nextOpenBracket = templateContent.IndexOf('<', openBracket + 1);
            int closeBracket = templateContent.IndexOf('>', openBracket);

            if (closeBracket == -1 || (nextOpenBracket != -1 && nextOpenBracket < closeBracket))
            {
                // Literal unclosed tag
                int endOfUnclosed = nextOpenBracket != -1 ? nextOpenBracket : templateContent.Length;
                nodes.Add(new TextNode(templateContent[openBracket..endOfUnclosed]));
                currentIndex = endOfUnclosed;
                continue;
            }

            string tagContent = templateContent.Substring(openBracket + 1, closeBracket - openBracket - 1).Trim();
            currentIndex = closeBracket + 1;

            if (tagContent.Length > MaxTokenLength)
            {
                nodes.Add(new TextNode($"<{tagContent}>"));
                continue;
            }

            // Standalone Directives
            if (string.Equals(tagContent, "Start", StringComparison.OrdinalIgnoreCase))
            {
                nodes.Add(new PrinterControlDirectiveNode(PrinterControlDirectiveType.Start));
            }
            else if (string.Equals(tagContent, "-", StringComparison.OrdinalIgnoreCase))
            {
                nodes.Add(new PrinterControlDirectiveNode(PrinterControlDirectiveType.HorizontalRule));
            }
            else if (string.Equals(tagContent, "Cut", StringComparison.OrdinalIgnoreCase))
            {
                nodes.Add(new PrinterControlDirectiveNode(PrinterControlDirectiveType.Cut));
            }
            // Scoped Formatting Directives
            else if (ScopedFormattingDirectives.Contains(tagContent))
            {
                string closingTag = $"</{tagContent}>";
                int closingIndex = templateContent.IndexOf(closingTag, currentIndex, StringComparison.OrdinalIgnoreCase);

                if (closingIndex != -1)
                {
                    string innerContent = templateContent[currentIndex..closingIndex];
                    var innerDoc = Parse(innerContent, strictValidation: false);
                    var directiveType = tagContent.ToLowerInvariant() switch
                    {
                        "bold" => FormattingDirectiveType.Bold,
                        "header" => FormattingDirectiveType.Header,
                        "nameset" => FormattingDirectiveType.Nameset,
                        "tnameset" => FormattingDirectiveType.TNameset,
                        "tnameset1" => FormattingDirectiveType.TNameset1,
                        _ => FormattingDirectiveType.Bold
                    };
                    nodes.Add(new FormattingDirectiveNode(directiveType, innerDoc.Nodes));
                    currentIndex = closingIndex + closingTag.Length;
                }
                else
                {
                    // Fallback to literal if closing tag not found
                    nodes.Add(new TextNode($"<{tagContent}>"));
                }
            }
            // Token Whitelist
            else if (CanonicalTokenMap.TryGetValue(tagContent, out var canonicalField))
            {
                nodes.Add(new TokenNode(tagContent, canonicalField));
            }
            else
            {
                // Unknown tag fallback to literal text
                nodes.Add(new TextNode($"<{tagContent}>"));
            }
        }

        return new TemplateDocument(nodes);
    }

    /// <inheritdoc />
    public string RenderToText(TemplateDocument document, WeighmentPrintData data, PrinterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(profile);

        var sb = new StringBuilder();
        RenderNodesToText(document.Nodes, data, profile, sb);
        return sb.ToString();
    }

    private void RenderNodesToText(IReadOnlyList<TemplateNode> nodes, WeighmentPrintData data, PrinterProfile profile, StringBuilder sb)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    sb.Append(text.Content);
                    break;

                case TokenNode token:
                    sb.Append(ResolveTokenValue(token.CanonicalField, data));
                    break;

                case FormattingDirectiveNode formatting:
                    // In plain text rendering, unwrap inner children
                    RenderNodesToText(formatting.Children, data, profile, sb);
                    break;

                case PrinterControlDirectiveNode control:
                    if (control.DirectiveType == PrinterControlDirectiveType.HorizontalRule)
                    {
                        int width = Math.Max(20, profile.PageWidthColumns);
                        sb.Append(new string('-', width));
                    }
                    // Start and Cut do not emit printable characters in plain text
                    break;
            }
        }
    }

    /// <inheritdoc />
    public byte[] RenderToBytes(TemplateDocument document, WeighmentPrintData data, PrinterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(profile);

        var encoding = profile.Encoding ?? Encoding.ASCII;

        if (profile.OutputMode == PrinterOutputMode.Gdi)
        {
            // GDI mode byte stream is plain encoded string
            string text = RenderToText(document, data, profile);
            return encoding.GetBytes(text);
        }

        // RawSpool mode: inject ESC/POS and control bytes
        using var ms = new MemoryStream();

        RenderNodesToByteStream(document.Nodes, data, profile, encoding, ms);
        return ms.ToArray();
    }

    private void RenderNodesToByteStream(
        IReadOnlyList<TemplateNode> nodes,
        WeighmentPrintData data,
        PrinterProfile profile,
        Encoding encoding,
        MemoryStream ms)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextNode text:
                    WriteEncodedString(ms, text.Content, encoding);
                    break;

                case TokenNode token:
                    string value = ResolveTokenValue(token.CanonicalField, data);
                    WriteEncodedString(ms, value, encoding);
                    break;

                case FormattingDirectiveNode formatting:
                    if (formatting.DirectiveType == FormattingDirectiveType.Bold)
                    {
                        // ESC E 1 (Bold ON)
                        ms.Write([0x1B, 0x45, 0x01], 0, 3);
                        RenderNodesToByteStream(formatting.Children, data, profile, encoding, ms);
                        // ESC E 0 (Bold OFF)
                        ms.Write([0x1B, 0x45, 0x00], 0, 3);
                    }
                    else
                    {
                        // Other formatting directives unwrap to children in raw spool
                        RenderNodesToByteStream(formatting.Children, data, profile, encoding, ms);
                    }
                    break;

                case PrinterControlDirectiveNode control:
                    switch (control.DirectiveType)
                    {
                        case PrinterControlDirectiveType.Start:
                            // ESC @ (Initialize printer)
                            ms.Write([0x1B, 0x40], 0, 2);
                            break;

                        case PrinterControlDirectiveType.Cut:
                            if (profile.SupportsCut)
                            {
                                // Line feeds before cut for thermal receipt margin
                                ms.Write([0x0A, 0x0A, 0x0A], 0, 3);
                                // GS V 'B' 0 (Full/Partial Cut)
                                ms.Write([0x1D, 0x56, 0x42, 0x00], 0, 4);
                            }
                            break;

                        case PrinterControlDirectiveType.HorizontalRule:
                            int width = Math.Max(20, profile.PageWidthColumns);
                            WriteEncodedString(ms, new string('-', width), encoding);
                            break;
                    }
                    break;
            }
        }
    }

    private static void WriteEncodedString(MemoryStream ms, string text, Encoding encoding)
    {
        if (string.IsNullOrEmpty(text)) return;
        byte[] bytes = encoding.GetBytes(text);
        ms.Write(bytes, 0, bytes.Length);
    }

    private static string ResolveTokenValue(string canonicalField, WeighmentPrintData data)
    {
        return canonicalField switch
        {
            "CompanyName" => data.CompanyName,
            "AddressLine1" => data.AddressLine1,
            "AddressLine2" => data.AddressLine2,
            "TransactionDate" => (data.CompletedAtLocal ?? data.OpenedAtLocal).ToString("dd/MM/yyyy"),
            "SlipNumber" => string.IsNullOrWhiteSpace(data.SlipNumber) ? "-" : data.SlipNumber,
            "PartyName" => string.IsNullOrWhiteSpace(data.PartyName) ? "-" : data.PartyName,
            "VehicleNumber" => string.IsNullOrWhiteSpace(data.VehicleNumber) ? "-" : data.VehicleNumber,
            "VehicleTypeName" => string.IsNullOrWhiteSpace(data.VehicleTypeName) ? "-" : data.VehicleTypeName,
            "MaterialName" => string.IsNullOrWhiteSpace(data.MaterialName) ? "-" : data.MaterialName,
            "TotalCharges" => data.TotalCharges.ToString("0.00"),
            "GrossWeightKg" => data.GrossWeightKg > 0 ? data.GrossWeightKg.ToString("F1") : "-",
            "GrossDate" => data.GrossCapturedAtLocal?.ToString("dd/MM/yyyy") ?? "-",
            "GrossTime" => data.GrossCapturedAtLocal?.ToString("HH:mm:ss") ?? "-",
            "TareWeightKg" => data.TareWeightKg > 0 ? data.TareWeightKg.ToString("F1") : "-",
            "TareDate" => data.TareCapturedAtLocal?.ToString("dd/MM/yyyy") ?? "-",
            "TareTime" => data.TareCapturedAtLocal?.ToString("HH:mm:ss") ?? "-",
            "NetWeightKg" => data.NetWeightKg > 0 ? data.NetWeightKg.ToString("F1") : "-",
            "NumberOfBags" => data.NumberOfBags.HasValue ? data.NumberOfBags.Value.ToString() : "-",
            "BagWeightKg" => data.BagWeightKg.HasValue ? data.BagWeightKg.Value.ToString("F2") : "-",
            "TotalBagWeightKg" => data.TotalBagWeightKg.HasValue ? data.TotalBagWeightKg.Value.ToString("F2") : "-",
            "ActualWeightKg" => data.ActualWeightKg.HasValue ? data.ActualWeightKg.Value.ToString("F1") : "-",
            "GatePassNumber" => string.IsNullOrWhiteSpace(data.GatePassNumber) ? "-" : data.GatePassNumber,
            "CustomField1" => string.IsNullOrWhiteSpace(data.CustomField1) ? "-" : data.CustomField1,
            "CustomField2" => string.IsNullOrWhiteSpace(data.CustomField2) ? "-" : data.CustomField2,
            "CustomField3" => string.IsNullOrWhiteSpace(data.CustomField3) ? "-" : data.CustomField3,
            "CustomField4" => string.IsNullOrWhiteSpace(data.CustomField4) ? "-" : data.CustomField4,
            "OperatorDisplayName" => string.IsNullOrWhiteSpace(data.OperatorDisplayName) ? "-" : data.OperatorDisplayName,
            "SlipType" => data.IsDuplicate
                ? (data.DuplicateWatermarkText ?? "WEIGHMENT SLIP (DUPLICATE)")
                : "WEIGHMENT SLIP",
            "Remarks" => string.IsNullOrWhiteSpace(data.Remarks) ? "-" : data.Remarks,
            _ => "-"
        };
    }
}
