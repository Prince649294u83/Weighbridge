namespace WeighBridge.Domain.Enums;

/// <summary>
/// Where a weighment has got to.
/// </summary>
/// <remarks>
/// <para>
/// Four states, not five. "First weight recorded" and "waiting for the second weight" are
/// the same fact seen from two sides — the moment the first weight is stored the record is
/// waiting for its second — so they are one state. A separate one could never be observed.
/// </para>
/// <para>
/// The values are persisted as integers. Never renumber them; add to the end.
/// </para>
/// </remarks>
public enum WeighmentStatus
{
    /// <summary>Vehicle details captured, no weight taken yet.</summary>
    Created = 0,

    /// <summary>First weight recorded; the vehicle must return for its second weight.</summary>
    AwaitingSecondWeight = 1,

    /// <summary>Both weights recorded and the net weight fixed. Terminal and immutable.</summary>
    Completed = 2,

    /// <summary>Abandoned before completion. Terminal; the row is kept for the audit trail.</summary>
    Cancelled = 3,
}
