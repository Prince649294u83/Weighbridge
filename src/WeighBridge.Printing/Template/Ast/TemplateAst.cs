namespace WeighBridge.Printing.Template.Ast;

/// <summary>
/// Abstract base node in a parsed print template Abstract Syntax Tree.
/// </summary>
public abstract record TemplateNode;

/// <summary>
/// Plain literal text content in a template.
/// </summary>
/// <param name="Content">Exact literal text including whitespace and line breaks.</param>
public sealed record TextNode(string Content) : TemplateNode;

/// <summary>
/// Dynamic placeholder token resolved at render time from canonical <see cref="WeighBridge.Core.Printing.WeighmentPrintData"/>.
/// </summary>
/// <param name="RawToken">The raw token string, e.g. "ticket", "gatepass", "acualwt".</param>
/// <param name="CanonicalField">The normalized canonical property name.</param>
public sealed record TokenNode(string RawToken, string CanonicalField) : TemplateNode;

/// <summary>
/// Formatting directive category.
/// </summary>
public enum FormattingDirectiveType
{
    /// <summary>Bold emphasis: &lt;Bold&gt;...&lt;/Bold&gt;.</summary>
    Bold,

    /// <summary>Main document header: &lt;Header&gt;...&lt;/Header&gt;.</summary>
    Header,

    /// <summary>Company name emphasis: &lt;Nameset&gt;...&lt;/Nameset&gt;.</summary>
    Nameset,

    /// <summary>Table header emphasis: &lt;TNameset&gt;...&lt;/TNameset&gt;.</summary>
    TNameset,

    /// <summary>Sub-table header emphasis: &lt;TNameset1&gt;...&lt;/TNameset1&gt;.</summary>
    TNameset1
}

/// <summary>
/// Scoped formatting directive node with nested children.
/// </summary>
/// <param name="DirectiveType">The formatting style type.</param>
/// <param name="Children">Child nodes enclosed within the directive tags.</param>
public sealed record FormattingDirectiveNode(
    FormattingDirectiveType DirectiveType,
    IReadOnlyList<TemplateNode> Children) : TemplateNode;

/// <summary>
/// Standalone hardware or layout control directive.
/// </summary>
public enum PrinterControlDirectiveType
{
    /// <summary>Paper cut directive: &lt;Cut&gt;.</summary>
    Cut,

    /// <summary>Document start / form feed directive: &lt;Start&gt;.</summary>
    Start,

    /// <summary>Horizontal divider / rule directive: &lt;-&gt;.</summary>
    HorizontalRule
}

/// <summary>
/// Standalone printer command directive node.
/// </summary>
/// <param name="DirectiveType">The control command type.</param>
public sealed record PrinterControlDirectiveNode(PrinterControlDirectiveType DirectiveType) : TemplateNode;

/// <summary>
/// Root container of a parsed template AST.
/// </summary>
/// <param name="Nodes">Sequential list of top-level AST nodes.</param>
public sealed record TemplateDocument(IReadOnlyList<TemplateNode> Nodes);
