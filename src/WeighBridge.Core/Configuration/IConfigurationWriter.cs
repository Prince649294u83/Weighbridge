namespace WeighBridge.Core.Configuration;

/// <summary>
/// Persists configuration values back to <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// The Settings screen showed the hardware, printer and reporting configuration as
/// read-only text and offered no way to change any of it — an operator whose indicator
/// moved to a different COM port had to find and hand-edit a JSON file in
/// <c>%LOCALAPPDATA%</c>. This is the seam that lets the screen write, without the view
/// model knowing that configuration is a file at all.
/// </para>
/// <para>
/// Keys are the same colon-delimited paths <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>
/// uses (<c>Hardware:WeightIndicator:PortName</c>), so there is no second addressing
/// scheme to learn and a caller can copy the key straight out of an options binding.
/// </para>
/// </remarks>
public interface IConfigurationWriter
{
    /// <summary>
    /// Writes each value at its configuration path, leaving every other key in the file —
    /// including protected secrets — exactly as it was.
    /// </summary>
    /// <param name="values">
    /// Configuration path to value. A <c>null</c> value removes the key. Values under a
    /// secret key name are encrypted before they reach the file.
    /// </param>
    /// <returns>The path of the file that was written.</returns>
    Task<string> SaveAsync(
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default);
}
