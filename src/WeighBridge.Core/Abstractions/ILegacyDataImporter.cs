namespace WeighBridge.Core.Abstractions;

/// <summary>
/// Summary report of records imported from a legacy database file.
/// </summary>
public sealed record LegacyImportResult(
    int PartiesImported,
    int MaterialsImported,
    int VehiclesImported,
    int WeighmentsImported,
    int ErrorsEncountered,
    IReadOnlyList<string> ErrorMessages)
{
    public bool HasErrors => ErrorsEncountered > 0 || ErrorMessages.Count > 0;
}

/// <summary>
/// Contract for importing historical master data and weighment transactions
/// from legacy systems (e.g. Microsoft Access .mdb databases or CSV archives).
/// </summary>
public interface ILegacyDataImporter
{
    /// <summary>
    /// Imports parties, materials, vehicles, and past weighments from the specified file.
    /// </summary>
    Task<LegacyImportResult> ImportAsync(
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
