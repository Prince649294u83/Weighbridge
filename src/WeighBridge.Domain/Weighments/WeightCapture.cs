using WeighBridge.Domain.Enums;

namespace WeighBridge.Domain.Weighments;

/// <summary>
/// One weight, with the two facts that make it defensible: when it was taken and where it
/// came from.
/// </summary>
/// <remarks>
/// <para>
/// A bare <c>decimal</c> on the weighment would let a weight be stored without its
/// timestamp or its provenance, because nothing would force the three to move together.
/// As one value they are set once, as a unit, or not at all.
/// </para>
/// <para>
/// Always kilograms. The unit is in the property name rather than in a companion string
/// column, because a unit column that nothing validates is a unit nobody can trust — a
/// slip reading "12.5" with a unit of "kg" recorded by a scale configured for tonnes is
/// indistinguishable from a correct record. Convert at the edge if an indicator reports
/// anything else.
/// </para>
/// <para>
/// Persisted as an EF Core owned type, so it is columns on the weighment row, not a join.
/// The range guard lives on <see cref="Weighment"/>'s transitions rather than here: this
/// type also has to be able to materialise whatever is already in the database, and a
/// constructor that threw would turn one bad legacy row into an application that cannot
/// open its own records.
/// </para>
/// </remarks>
/// <param name="Kilograms">The weight in kilograms.</param>
/// <param name="CapturedAtUtc">When the reading was taken.</param>
/// <param name="Source">Whether the indicator supplied it or an operator typed it.</param>
public sealed record WeightCapture(decimal Kilograms, DateTime CapturedAtUtc, WeightSource Source)
{
    /// <summary>True when an operator typed this figure instead of the indicator supplying it.</summary>
    public bool IsManual => Source == WeightSource.Manual;

    /// <inheritdoc />
    public override string ToString() => $"{Kilograms:0.##} kg ({Source})";
}
