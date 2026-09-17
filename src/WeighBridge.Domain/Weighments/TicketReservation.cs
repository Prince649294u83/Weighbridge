using WeighBridge.Domain.Common;

namespace WeighBridge.Domain.Weighments;

/// <summary>
/// Status of an authoritative ticket reservation.
/// </summary>
public enum TicketReservationStatus
{
    /// <summary>
    /// The ticket number has been persistently reserved in SQLite and is displayed to the operator.
    /// No actual weighment transaction exists yet.
    /// </summary>
    Reserved = 0,

    /// <summary>
    /// The reserved ticket has been consumed and bound to an actual weighment upon recording the first weight.
    /// </summary>
    Consumed = 1,

    /// <summary>
    /// The reservation was explicitly cancelled or discarded before any weight was recorded.
    /// </summary>
    Cancelled = 2,
}

/// <summary>
/// Authoritative persistent ticket reservation aggregate.
/// </summary>
/// <remarks>
/// <para>
/// Represents a reserved ticket number allocated immediately upon initiating First Entry (F1).
/// This separates ticket sequence allocation from the <see cref="Weighment"/> aggregate root,
/// ensuring that a valid ticket is persistently visible to the operator without creating an
/// incomplete or invalid weighment record.
/// </para>
/// <para>
/// The sequence is derived directly from the database identity (<see cref="EntityBase.Id"/>) via
/// <see cref="SlipNumbers.Format"/>, ensuring strict monotonicity and uniqueness per database instance.
/// </para>
/// </remarks>
public sealed class TicketReservation : EntityBase, IAggregateRoot
{
    public const int MaxReasonLength = 512;
    public const int MaxTerminalIdLength = 64;

    /// <summary>For EF Core materialisation only.</summary>
    private TicketReservation()
    {
    }

    /// <summary>Creates a new ticket reservation in the <see cref="TicketReservationStatus.Reserved"/> state.</summary>
    public static TicketReservation Create(
        string? tentativeVehicleNumber = null,
        string? terminalId = null,
        string? operatorName = null)
    {
        var reservation = new TicketReservation
        {
            TentativeVehicleNumber = string.IsNullOrWhiteSpace(tentativeVehicleNumber)
                ? null
                : Weighment.NormaliseVehicleNumber(tentativeVehicleNumber),
            TerminalId = string.IsNullOrWhiteSpace(terminalId) ? null : terminalId.Trim(),
            Status = TicketReservationStatus.Reserved,
            CreatedBy = operatorName?.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            Version = Guid.NewGuid(),
        };

        return reservation;
    }

    /// <summary>
    /// Human-readable slip number (e.g. <c>WB-000042</c>). Derived from <see cref="EntityBase.Id"/>.
    /// </summary>
    public string SlipNumber { get; private set; } = string.Empty;

    /// <summary>Lifecycle state of this reservation.</summary>
    public TicketReservationStatus Status { get; private set; } = TicketReservationStatus.Reserved;

    /// <summary>Optional tentative vehicle number entered when the reservation was created.</summary>
    public string? TentativeVehicleNumber { get; private set; }

    /// <summary>Identifier of the terminal/workstation that created the reservation.</summary>
    public string? TerminalId { get; private set; }

    /// <summary>Foreign key to the <see cref="Weighment"/> that consumed this reservation, if consumed.</summary>
    public long? ConsumedByWeighmentId { get; private set; }

    /// <summary>UTC timestamp when this reservation was consumed into a real weighment.</summary>
    public DateTime? ConsumedAtUtc { get; private set; }

    /// <summary>Login name of the operator who consumed the reservation.</summary>
    public string? ConsumedBy { get; private set; }

    /// <summary>UTC timestamp when this reservation was explicitly cancelled, if cancelled.</summary>
    public DateTime? CancelledAtUtc { get; private set; }

    /// <summary>Audited operator reason for cancelling this reservation.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>Optimistic concurrency token.</summary>
    public Guid Version { get; private set; } = Guid.NewGuid();

    /// <summary>
    /// Derives and assigns the canonical <see cref="SlipNumber"/> from the persistent <see cref="EntityBase.Id"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The reservation is transient or already has a slip number.</exception>
    public void AssignSlipNumber()
    {
        if (IsTransient)
        {
            throw new InvalidOperationException(
                "A slip number cannot be assigned before the reservation has been saved and given an identity.");
        }

        if (!string.IsNullOrEmpty(SlipNumber))
        {
            throw new InvalidOperationException(
                $"Ticket reservation {SlipNumber} already has a slip number; it cannot be renumbered.");
        }

        SlipNumber = SlipNumbers.Format(Id);
    }

    /// <summary>
    /// Atomically marks this reservation as consumed by a real weighment.
    /// </summary>
    /// <param name="weighmentId">Primary key of the created weighment.</param>
    /// <param name="consumedBy">Login name of the operator recording the first weight.</param>
    /// <exception cref="InvalidOperationException">The reservation is not in the <see cref="TicketReservationStatus.Reserved"/> state.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="weighmentId"/> is not positive.</exception>
    public void Consume(long weighmentId, string? consumedBy = null)
    {
        if (Status != TicketReservationStatus.Reserved)
        {
            throw new InvalidOperationException(
                $"Reservation {SlipNumber} cannot be consumed because it is in '{Status}' status.");
        }

        if (weighmentId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(weighmentId), weighmentId, "Consumed weighment ID must be positive.");
        }

        ConsumedByWeighmentId = weighmentId;
        ConsumedAtUtc = DateTime.UtcNow;
        ConsumedBy = string.IsNullOrWhiteSpace(consumedBy) ? null : consumedBy.Trim();
        Status = TicketReservationStatus.Consumed;
        Version = Guid.NewGuid();
    }

    /// <summary>
    /// Explicitly cancels an unused reservation with an audit reason.
    /// </summary>
    /// <param name="reason">Mandatory operator reason for cancellation.</param>
    /// <param name="cancelledBy">Login name of the operator cancelling the reservation.</param>
    /// <exception cref="InvalidOperationException">The reservation is not in the <see cref="TicketReservationStatus.Reserved"/> state.</exception>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is null or empty.</exception>
    public void Cancel(string reason, string? cancelledBy = null)
    {
        if (Status != TicketReservationStatus.Reserved)
        {
            throw new InvalidOperationException(
                $"Reservation {SlipNumber} cannot be cancelled because it is in '{Status}' status.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var trimmedReason = reason.Trim();
        if (trimmedReason.Length > MaxReasonLength)
        {
            throw new ArgumentException($"Cancellation reason must be {MaxReasonLength} characters or fewer.", nameof(reason));
        }

        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = trimmedReason;
        Status = TicketReservationStatus.Cancelled;
        ModifiedBy = string.IsNullOrWhiteSpace(cancelledBy) ? null : cancelledBy.Trim();
        ModifiedAtUtc = DateTime.UtcNow;
        Version = Guid.NewGuid();
    }
}
