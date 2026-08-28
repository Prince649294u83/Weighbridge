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
}
