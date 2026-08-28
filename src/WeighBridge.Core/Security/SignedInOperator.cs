namespace WeighBridge.Core.Security;

/// <summary>
/// The account name of the operator who signed in, published for the log enrichment.
/// </summary>
/// <remarks>
/// <para>
/// One shared field rather than a lookup, because asking <see cref="IPermissionService"/>
/// directly would be a dependency cycle: the permission service writes to the log, so a
/// logger that resolved the permission service would be resolving it from inside its own
/// constructor — and the container would build a second one, then a third, until the stack
/// ran out.
/// </para>
/// <para>
/// Written by the permission service whenever the operator changes and read once per log
/// entry, so an entry written after a sign-in names the operator who caused it instead of
/// the Windows account the terminal happens to run under. That distinction is the whole
/// point of the audit trail on a terminal several operators share across a shift.
/// </para>
/// </remarks>
public sealed class SignedInOperator
{
    private string? _userName;

    /// <summary>
    /// The signed-in operator's account name, or <c>null</c> before anyone has signed in.
    /// </summary>
    /// <remarks>
    /// A reference assignment is already atomic, so a reader sees either the old name or the
    /// new one and never a torn value; the volatile access is here so a thread that never
    /// synchronises with the writer still observes the change rather than a cached field.
    /// </remarks>
    public string? UserName
    {
        get => Volatile.Read(ref _userName);
        set => Volatile.Write(ref _userName, value);
    }
}
