using WeighBridge.Core.Printing;
using WeighBridge.Printing.Template.Ast;

namespace WeighBridge.Printing.Template;

/// <summary>
/// Service contract for parsing, validating, and rendering weighment slip templates.
/// </summary>
public interface ITemplateEngine
{
    /// <summary>
    /// Parses template text into an Abstract Syntax Tree document.
    /// </summary>
    /// <param name="templateContent">Raw template file text.</param>
    /// <param name="strictValidation">If true, throws <see cref="TemplateParseException"/> on unknown tokens or syntax errors.</param>
    /// <returns>Parsed AST document.</returns>
    TemplateDocument Parse(string templateContent, bool strictValidation = false);

    /// <summary>
    /// Validates template syntax, tokens, and capability compatibility against an optional printer profile.
    /// </summary>
    /// <param name="templateContent">Raw template file text.</param>
    /// <param name="profile">Optional target printer profile to check directive compatibility against.</param>
    /// <returns>Validation result containing diagnostic errors.</returns>
    TemplateValidationResult Validate(string templateContent, PrinterProfile? profile = null);

    /// <summary>
    /// Renders an AST document into normalized plain text using canonical weighment data.
    /// </summary>
    /// <param name="document">Parsed AST document.</param>
    /// <param name="data">Authoritative weighment data DTO.</param>
    /// <param name="profile">Target printer profile.</param>
    /// <returns>Rendered text document.</returns>
    string RenderToText(TemplateDocument document, WeighmentPrintData data, PrinterProfile profile);

    /// <summary>
    /// Renders an AST document into an encoded byte stream for raw spooling (including ESC/POS control bytes).
    /// </summary>
    /// <param name="document">Parsed AST document.</param>
    /// <param name="data">Authoritative weighment data DTO.</param>
    /// <param name="profile">Target printer profile.</param>
    /// <returns>Byte array ready for raw Windows Print Spooler submission.</returns>
    byte[] RenderToBytes(TemplateDocument document, WeighmentPrintData data, PrinterProfile profile);
}
