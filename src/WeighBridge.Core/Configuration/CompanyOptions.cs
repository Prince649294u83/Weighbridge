namespace WeighBridge.Core.Configuration;

/// <summary>
/// Bound to the <c>Company</c> section of <c>appsettings.json</c>.
/// Provides header details for printed slips and reports.
/// </summary>
public sealed class CompanyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Company";

    /// <summary>Legal or trading name of the company.</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>First address line.</summary>
    public string AddressLine1 { get; set; } = string.Empty;

    /// <summary>Second address line.</summary>
    public string AddressLine2 { get; set; } = string.Empty;

    /// <summary>Contact telephone number.</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Contact email address.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>GSTIN / Tax identifier.</summary>
    public string TaxId { get; set; } = string.Empty;
}
