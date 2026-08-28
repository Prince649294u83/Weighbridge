namespace WeighBridge.Core.Undo;

/// <summary>
/// How much history the undo manager keeps.
/// </summary>
public sealed class UndoOptions
{
    /// <summary>Configuration section these values bind from.</summary>
    public const string SectionName = "Undo";

    private int _maxDepth = 20;

    /// <summary>
    /// How many entries to keep before the oldest is dropped.
    /// </summary>
    /// <remarks>
    /// Bounded because this process runs for months. Each entry holds a command, and a
    /// command holds whatever it needs to compensate itself — an unbounded history is a leak
    /// that shows up as a memory graph climbing over a shift.
    /// </remarks>
    public int MaxDepth
    {
        get => _maxDepth;

        // Floored at one: a depth of zero would accept a push and immediately drop it,
        // leaving CanUndo false right after an operation that said it was undoable.
        set => _maxDepth = Math.Max(1, value);
    }
}
