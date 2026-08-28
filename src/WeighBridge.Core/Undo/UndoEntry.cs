namespace WeighBridge.Core.Undo;

/// <summary>
/// One undoable thing that happened, and when.
/// </summary>
/// <param name="Command">The command that can put it back.</param>
/// <param name="Description">What the operator sees in the history.</param>
/// <param name="ExecutedAt">When it happened.</param>
/// <param name="CorrelationId">
/// Correlation identifier of the operation that produced it, so an entry in the undo
/// history can be traced to the log lines and audit record it belongs to.
/// </param>
public sealed record UndoEntry(
    IUndoableCommand Command,
    string Description,
    DateTimeOffset ExecutedAt,
    string? CorrelationId)
{
    /// <inheritdoc />
    public override string ToString() => $"{Description} at {ExecutedAt:HH:mm:ss}";
}

/// <summary>
/// Several commands undone and redone as one.
/// </summary>
/// <remarks>
/// What an import or a bulk edit produces. Undoing in reverse order matters: if creating a
/// vehicle then a rate for it was the sequence, the rate has to go before the vehicle does.
/// </remarks>
public sealed class CompoundUndoableCommand : IUndoableCommand
{
    private readonly IReadOnlyList<IUndoableCommand> _commands;

    /// <summary>Creates a compound of at least one command.</summary>
    public CompoundUndoableCommand(string description, IEnumerable<IUndoableCommand> commands)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(commands);

        _commands = [.. commands];

        if (_commands.Count == 0)
        {
            throw new ArgumentException("A compound needs at least one command.", nameof(commands));
        }

        UndoDescription = description;
    }

    /// <inheritdoc />
    public string UndoDescription { get; }

    /// <summary>How many commands this compound holds.</summary>
    public int Count => _commands.Count;

    /// <inheritdoc />
    public async Task UndoAsync(CancellationToken cancellationToken = default)
    {
        // Reverse order. Undoing forwards would remove a record something later in the
        // batch still points at.
        for (var index = _commands.Count - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _commands[index].UndoAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RedoAsync(CancellationToken cancellationToken = default)
    {
        foreach (var command in _commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await command.RedoAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
