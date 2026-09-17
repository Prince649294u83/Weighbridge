using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Core.Abstractions;

/// <summary>
/// What the operator supplies to open a weighment.
/// </summary>
/// <remarks>
/// A separate type from <see cref="Weighment"/> so the ViewModel has something to bind to
/// and something to validate before an aggregate exists. The aggregate's constructor
/// enforces invariants; this carries the operator's intent, valid or not, up to the point
/// where it is checked.
/// </remarks>
public sealed record NewWeighment
{
    /// <summary>Registration number of the vehicle. Required.</summary>
    public string VehicleNumber { get; init; } = string.Empty;

    /// <summary>Whether the vehicle arrives loaded (gross first) or empty (tare first).</summary>
    public WeighmentMode Mode { get; init; } = WeighmentMode.GrossFirst;

    /// <summary>Customer the load belongs to.</summary>
    public string? PartyName { get; init; }

    /// <summary>Material being carried.</summary>
    public string? MaterialName { get; init; }

    /// <summary>Driver's name.</summary>
    public string? DriverName { get; init; }

    /// <summary>Transport company.</summary>
    public string? TransporterName { get; init; }

    /// <summary>Anything else that belongs on the record.</summary>
    public string? Remarks { get; init; }

    /// <summary>First-entry weighbridge fee collected from vehicle in rupees.</summary>
    public decimal Charges { get; init; } = 0m;

    /// <summary>Count of packages / bags for deduction calculation.</summary>
    public int? NumberOfBags { get; init; }

    /// <summary>Tare weight per bag in kilograms.</summary>
    public decimal? BagWeightKg { get; init; }

    /// <summary>External security gate pass reference number.</summary>
    public string? GatePassNumber { get; init; }

    /// <summary>User-configured field 1 (e.g. Consigner / Container No), locked in F2.</summary>
    public string? CustomField1 { get; init; }

    /// <summary>User-configured field 2 (e.g. Consignee / Seal No), locked in F2.</summary>
    public string? CustomField2 { get; init; }

    /// <summary>Optional master record foreign key for vehicle.</summary>
    public long? VehicleId { get; init; }

    /// <summary>Optional master record foreign key for party.</summary>
    public long? PartyId { get; init; }

    /// <summary>Optional master record foreign key for material.</summary>
    public long? MaterialId { get; init; }

    /// <summary>Optional master record foreign key for vehicle type.</summary>
    public long? VehicleTypeId { get; init; }

    /// <summary>Optional vehicle type name snapshot.</summary>
    public string? VehicleTypeName { get; init; }
}

/// <summary>
/// Carries second-entry details to update on an open weighment awaiting second weight.
/// </summary>
public sealed record UpdateSecondEntryDetailsRequest(
    long WeighmentId,
    decimal SecondCharges,
    int? NumberOfBags,
    decimal? BagWeightKg,
    string? GatePassNumber,
    string? Remarks,
    string? CustomField3 = null,
    string? CustomField4 = null,
    Guid? ExpectedVersion = null);

/// <summary>
/// Carries second weight capture and optional second-entry details to complete a weighment.
/// </summary>
public sealed record RecordSecondWeightRequest(
    long WeighmentId,
    decimal Kilograms,
    WeightSource Source,
    decimal SecondCharges = 0m,
    int? NumberOfBags = null,
    decimal? BagWeightKg = null,
    string? GatePassNumber = null,
    string? Remarks = null,
    string? CustomField3 = null,
    string? CustomField4 = null,
    Guid? ExpectedVersion = null);

/// <summary>
/// Captures a one-step weighment where the second weight is supplied by an approved tare source.
/// </summary>
public sealed record RecordSingleEntryWeightRequest(
    long WeighmentId,
    decimal Kilograms,
    WeightSource Source,
    decimal TareWeightKg,
    Guid? ExpectedVersion = null);

/// <summary>
/// Everything the application does to a weighment.
/// </summary>
/// <remarks>
/// <para>
/// The boundary between the business and the database. ViewModels depend on this and never
/// on a repository, a unit of work or a <c>DbContext</c> — which is what keeps SQL out of
/// the presentation layer as the architecture requires.
/// </para>
/// <para>
/// Each method is one transaction and one business fact. The implementation opens its own
/// unit of work per call rather than holding one, because a long-lived change tracker in a
/// desktop application that stays open for weeks accumulates every row an operator has ever
/// looked at.
/// </para>
/// <para>
/// Rules the operator can fix — a missing vehicle number, an implausible weight — are
/// checked by validators before the call. What this layer enforces is what must never be
/// true regardless of input: it lets the aggregate refuse an impossible transition and
/// reports the refusal, rather than checking the same thing twice in a place that could
/// drift.
/// </para>
/// </remarks>
public interface IWeighmentService
{
    /// <summary>
    /// Opens a weighment and allocates its slip number.
    /// </summary>
    /// <returns>The saved weighment, with its identity and slip number populated.</returns>
    Task<Weighment> CreateAsync(NewWeighment request, CancellationToken cancellationToken = default);

    /// <summary>Records the first weight against an open weighment.</summary>
    /// <exception cref="InvalidOperationException">
    /// No such weighment, or it is not awaiting its first weight.
    /// </exception>
    Task<Weighment> RecordFirstWeightAsync(
        long weighmentId,
        decimal kilograms,
        WeightSource source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates second-entry operational fields on an open transaction awaiting second weight.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No such weighment, it is not awaiting its second weight, or a concurrency conflict occurred.
    /// </exception>
    Task<Weighment> UpdateSecondEntryDetailsAsync(
        UpdateSecondEntryDetailsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the second weight with typed request parameters, fixing the net weight and completing the transaction.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No such weighment, it is not awaiting its second weight, or the two weights do not
    /// yield a valid net weight.
    /// </exception>
    Task<Weighment> RecordSecondWeightAsync(
        RecordSecondWeightRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the second weight, which fixes the net weight and completes the weighment.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No such weighment, it is not awaiting its second weight, or the two weights do not
    /// yield a positive net.
    /// </exception>
    Task<Weighment> RecordSecondWeightAsync(
        long weighmentId,
        decimal kilograms,
        WeightSource source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the only live weight for a single-entry weighment and completes it using an approved tare value.
    /// </summary>
    Task<Weighment> RecordSingleEntryWeightAsync(
        RecordSingleEntryWeightRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up a single open transaction waiting for second weight by human-entered search text
    /// (canonical slip number or vehicle registration).
    /// </summary>
    /// <returns>
    /// The matching <see cref="Weighment"/> aggregate if exactly one pending record matches;
    /// <c>null</c> if no pending transaction matches; or throws if multiple pending records match a vehicle.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Multiple pending transactions match the given vehicle number.
    /// </exception>
    Task<Weighment?> FindPendingSecondEntryAsync(
        string searchKey,
        CancellationToken cancellationToken = default);

    /// <summary>Abandons an open weighment, keeping the row and the reason.</summary>
    /// <exception cref="InvalidOperationException">
    /// No such weighment, or it is already finished.
    /// </exception>
    Task<Weighment> CancelAsync(
        long weighmentId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one weighment by identity, or <c>null</c>.</summary>
    Task<Weighment?> GetAsync(long weighmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one weighment by slip number, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Accepts what an operator would type: <c>42</c>, <c>000042</c> and <c>wb-42</c> all
    /// find <c>WB-000042</c>.
    /// </remarks>
    Task<Weighment?> GetBySlipNumberAsync(string slipNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the weighments waiting for their second weight, newest first.
    /// </summary>
    /// <remarks>
    /// The working list of the Vehicle Entry screen: every vehicle currently on site with a
    /// first weight against it.
    /// </remarks>
    Task<IReadOnlyList<Weighment>> GetAwaitingSecondWeightAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the most recently opened weighments, newest first.</summary>
    Task<IReadOnlyList<Weighment>> GetRecentAsync(int count = 20, CancellationToken cancellationToken = default);

    /// <summary>Attaches captured image metadata to an existing weighment.</summary>
    Task<WeighmentImage> AttachImageAsync(
        long weighmentId,
        string cameraName,
        string stage,
        CameraSource source,
        string relativeFilePath,
        DateTime capturedAtUtc,
        long fileSizeBytes,
        string? sha256Checksum = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all images attached to a weighment.</summary>
    Task<IReadOnlyList<WeighmentImage>> GetImagesAsync(long weighmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates an authoritative persistent ticket reservation in SQLite.
    /// </summary>
    Task<TicketReservation> ReserveTicketAsync(
        string? tentativeVehicleNumber = null,
        string? terminalId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns one ticket reservation by identity, or <c>null</c>.</summary>
    Task<TicketReservation?> GetReservationAsync(long reservationId, CancellationToken cancellationToken = default);

    /// <summary>Returns one ticket reservation by slip number, or <c>null</c>.</summary>
    Task<TicketReservation?> GetReservationBySlipNumberAsync(string slipNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active unconsumed ticket reservation for the terminal or user, if one already exists.
    /// </summary>
    Task<TicketReservation?> GetActiveReservationAsync(string? terminalId = null, CancellationToken cancellationToken = default);

    /// <summary>Cancels an unconsumed ticket reservation with an audited reason.</summary>
    Task<TicketReservation> CancelReservationAsync(
        long reservationId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates a new weighment from a reservation and records the first weight in one transaction.
    /// </summary>
    Task<Weighment> CreateWithReservationAndRecordFirstWeightAsync(
        long reservationId,
        NewWeighment request,
        decimal kilograms,
        WeightSource source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates a single-entry weighment from a reservation and completes it in one transaction.
    /// </summary>
    Task<Weighment> CreateWithReservationAndRecordSingleEntryWeightAsync(
        long reservationId,
        NewWeighment request,
        decimal kilograms,
        WeightSource source,
        decimal tareWeightKg,
        CancellationToken cancellationToken = default);
}
