using WeighBridge.Domain.Common;
using WeighBridge.Domain.Enums;

namespace WeighBridge.Domain.Weighments;

/// <summary>
/// One vehicle across the weighbridge: the record a slip is printed from.
/// </summary>
/// <remarks>
/// <para>
/// The aggregate root of the business. A weighbridge does exactly one thing — weigh a
/// vehicle twice and report the difference — and this type owns the whole of it: the two
/// weights, which of them is the gross, the net that follows from them, and the lifecycle
/// that says which of those may be set when.
/// </para>
/// <para>
/// Every state change goes through a method on this class. There are no public setters,
/// because the rules being enforced are not validation in the "tell the operator to try
/// again" sense — they are invariants. A weighment with a second weight and no first, or a
/// completed one with a net weight that does not equal gross minus tare, is not a bad input
/// to be rejected at the edge; it is a record that must not be capable of existing. So the
/// only route to a weight is <see cref="RecordFirstWeight"/> or
/// <see cref="RecordSecondWeight"/>, and both refuse from the wrong state.
/// </para>
/// <para>
/// Party, material, driver and transporter are held as text rather than as foreign keys to
/// master tables that do not exist yet — and will stay text when those tables arrive. A
/// weighment slip is a legal record of what was agreed at the time it was printed: if
/// renaming a customer three years later silently rewrote every historical slip that
/// mentioned them, the slips would no longer be evidence of anything. The master tables
/// will supply the pick lists; the slip keeps what was picked.
/// </para>
/// <para>
/// Soft-deletable, never physically removed. See <see cref="ISoftDeletable"/>.
/// </para>
/// </remarks>
public sealed class Weighment : EntityBase, IAggregateRoot, ISoftDeletable
{
    /// <summary>
    /// Largest weight the domain will accept, in kilograms.
    /// </summary>
    /// <remarks>
    /// Not a scale specification — a typo filter. No road vehicle crossing a weighbridge
    /// weighs 200 tonnes, so a reading above it is a mistyped decimal point or a garbled
    /// serial frame, and either is worth refusing before it reaches a printed slip.
    /// </remarks>
    public const decimal MaximumWeightKg = 200_000m;

    /// <summary>Longest vehicle registration the domain will accept.</summary>
    public const int VehicleNumberMaxLength = 24;

    /// <summary>Longest party, material, driver or transporter name the domain will accept.</summary>
    public const int NameMaxLength = 128;

    /// <summary>Longest free-text remark or cancellation reason the domain will accept.</summary>
    public const int TextMaxLength = 512;

    /// <summary>For EF Core materialisation only.</summary>
    private Weighment()
    {
    }

    /// <summary>
    /// Opens a weighment. No weight is taken yet, so the record starts at
    /// <see cref="WeighmentStatus.Created"/>.
    /// </summary>
    /// <remarks>
    /// The slip number is not assigned here. It is derived from the identity the database
    /// allocates, so it can only be set once the row exists — see
    /// <see cref="AssignSlipNumber"/>.
    /// </remarks>
    /// <param name="vehicleNumber">Registration number of the vehicle. Required.</param>
    /// <param name="mode">Whether the vehicle arrives loaded or empty.</param>
    /// <param name="partyName">Customer the load belongs to.</param>
    /// <param name="materialName">What is being carried.</param>
    /// <param name="driverName">Driver's name.</param>
    /// <param name="transporterName">Transport company.</param>
    /// <param name="remarks">Anything else the operator needs on the record.</param>
    /// <param name="vehicleId">Optional master record foreign key for vehicle.</param>
    /// <param name="partyId">Optional master record foreign key for party.</param>
    /// <param name="materialId">Optional master record foreign key for material.</param>
    /// <param name="vehicleTypeId">Optional master record foreign key for vehicle type.</param>
    /// <param name="vehicleTypeName">Optional snapshot of vehicle type name.</param>
    public static Weighment Open(
        string vehicleNumber,
        WeighmentMode mode,
        string? partyName = null,
        string? materialName = null,
        string? driverName = null,
        string? transporterName = null,
        string? remarks = null,
        long? vehicleId = null,
        long? partyId = null,
        long? materialId = null,
        long? vehicleTypeId = null,
        string? vehicleTypeName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleNumber);

        return new Weighment
        {
            // Registrations are quoted, radioed and searched for. Storing them one way means
            // a search never misses a match over a lower-case letter or a stray space.
            VehicleNumber = NormaliseVehicleNumber(vehicleNumber),
            Mode = mode,
            Status = WeighmentStatus.Created,
            PartyName = Trim(partyName),
            MaterialName = Trim(materialName),
            DriverName = Trim(driverName),
            TransporterName = Trim(transporterName),
            Remarks = Trim(remarks),
            VehicleId = vehicleId,
            PartyId = partyId,
            MaterialId = materialId,
            VehicleTypeId = vehicleTypeId,
            VehicleTypeName = Trim(vehicleTypeName),
        };
    }

    /// <summary>Human-readable slip number, for example <c>WB-000042</c>. Unique.</summary>
    /// <remarks>Empty only between the insert that allocates the identity and the update
    /// that derives this from it, inside one transaction.</remarks>
    public string SlipNumber { get; private set; } = string.Empty;

    /// <summary>Registration number of the vehicle, upper-cased and stripped of spaces.</summary>
    public string VehicleNumber { get; private set; } = string.Empty;

    /// <summary>Optional foreign key to <see cref="WeighBridge.Domain.Masters.Vehicle"/> master.</summary>
    public long? VehicleId { get; private set; }

    /// <summary>Optional foreign key to <see cref="WeighBridge.Domain.Masters.Party"/> master.</summary>
    public long? PartyId { get; private set; }

    /// <summary>Optional foreign key to <see cref="WeighBridge.Domain.Masters.Material"/> master.</summary>
    public long? MaterialId { get; private set; }

    /// <summary>Optional foreign key to <see cref="WeighBridge.Domain.Masters.VehicleType"/> master.</summary>
    public long? VehicleTypeId { get; private set; }

    /// <summary>Snapshot of the vehicle type at the time of weighing.</summary>
    public string? VehicleTypeName { get; private set; }

    /// <summary>Whether the first weight is the gross or the tare.</summary>
    public WeighmentMode Mode { get; private set; }

    /// <summary>Where this weighment has got to.</summary>
    public WeighmentStatus Status { get; private set; }

    /// <summary>Customer the load belongs to, as recorded at the time.</summary>
    public string? PartyName { get; private set; }

    /// <summary>Material being carried, as recorded at the time.</summary>
    public string? MaterialName { get; private set; }

    /// <summary>Driver's name, as recorded at the time.</summary>
    public string? DriverName { get; private set; }

    /// <summary>Transport company, as recorded at the time.</summary>
    public string? TransporterName { get; private set; }

    /// <summary>Free-text note from the operator.</summary>
    public string? Remarks { get; private set; }

    /// <summary>The first weight taken, or <c>null</c> before the vehicle has been weighed.</summary>
    public WeightCapture? FirstWeight { get; private set; }

    /// <summary>The second weight taken, or <c>null</c> until the vehicle returns.</summary>
    public WeightCapture? SecondWeight { get; private set; }

    /// <summary>
    /// Gross minus tare, fixed at completion.
    /// </summary>
    /// <remarks>
    /// Stored rather than recomputed on read. The net is what the slip says and what the
    /// invoice is raised against; a figure derived at display time would silently change if
    /// the arithmetic ever did.
    /// </remarks>
    public decimal? NetWeightKg { get; private set; }

    /// <summary>When the second weight completed the weighment.</summary>
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>Why the weighment was abandoned.</summary>
    public string? CancellationReason { get; private set; }

    /// <summary>When the weighment was abandoned.</summary>
    public DateTime? CancelledAtUtc { get; private set; }

    /// <inheritdoc />
    public bool IsDeleted { get; set; }

    /// <inheritdoc />
    public DateTime? DeletedAtUtc { get; set; }

    /// <inheritdoc />
    public string? DeletedBy { get; set; }

    /// <summary>The gross weight, or <c>null</c> while it has not been taken.</summary>
    public WeightCapture? Gross => Mode == WeighmentMode.GrossFirst ? FirstWeight : SecondWeight;

    /// <summary>The tare weight, or <c>null</c> while it has not been taken.</summary>
    public WeightCapture? Tare => Mode == WeighmentMode.GrossFirst ? SecondWeight : FirstWeight;

    private readonly List<WeighmentImage> _images = [];

    /// <summary>Images captured during this weighment transaction.</summary>
    public IReadOnlyCollection<WeighmentImage> Images => _images.AsReadOnly();

    /// <summary>True while the weighment can still be worked on.</summary>
    public bool IsOpen => Status is WeighmentStatus.Created or WeighmentStatus.AwaitingSecondWeight;

    /// <summary>What the operator has to do next, in the words the screen uses.</summary>
    public string NextAction => Status switch
    {
        WeighmentStatus.Created => Mode == WeighmentMode.GrossFirst
            ? "Record gross weight"
            : "Record tare weight",
        WeighmentStatus.AwaitingSecondWeight => Mode == WeighmentMode.GrossFirst
            ? "Record tare weight"
            : "Record gross weight",
        WeighmentStatus.Completed => "Print slip",
        _ => "None",
    };

    /// <summary>
    /// Derives the slip number from the identity the database allocated.
    /// </summary>
    /// <remarks>
    /// Callable once. A slip number that changed after a slip was printed would leave two
    /// different pieces of paper claiming to be the same weighment.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The entity has no identity yet, or already has a slip number.
    /// </exception>
    public void AssignSlipNumber()
    {
        if (IsTransient)
        {
            throw new InvalidOperationException(
                "A slip number cannot be assigned before the weighment has been saved and given an identity.");
        }

        if (!string.IsNullOrEmpty(SlipNumber))
        {
            throw new InvalidOperationException(
                $"Weighment {SlipNumber} already has a slip number; it cannot be renumbered.");
        }

        SlipNumber = SlipNumbers.Format(Id);
    }

    /// <summary>
    /// Records the first weight and starts waiting for the second.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The weighment is not <see cref="WeighmentStatus.Created"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The weight is not plausible.</exception>
    public void RecordFirstWeight(WeightCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        GuardWeight(capture.Kilograms);

        if (Status != WeighmentStatus.Created)
        {
            throw new InvalidOperationException(
                $"The first weight has already been recorded for {Describe()}; its status is {Status}.");
        }

        FirstWeight = capture;
        Status = WeighmentStatus.AwaitingSecondWeight;
    }

    /// <summary>
    /// Records the second weight, fixes the net weight and completes the weighment.
    /// </summary>
    /// <remarks>
    /// Recording the second weight <em>is</em> completion. There is no state between the
    /// two: a weighment holding both weights but not yet completed would be waiting for an
    /// operator action that does not exist, and every report would have to decide what to
    /// do with it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The weighment is not <see cref="WeighmentStatus.AwaitingSecondWeight"/>, or the two
    /// weights do not yield a positive net.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The weight is not plausible.</exception>
    public void RecordSecondWeight(WeightCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        GuardWeight(capture.Kilograms);

        if (Status != WeighmentStatus.AwaitingSecondWeight)
        {
            throw new InvalidOperationException(
                Status == WeighmentStatus.Created
                    ? $"The first weight has not been recorded for {Describe()}, so there is no second weight to take."
                    : $"{Describe()} is {Status} and cannot take another weight.");
        }

        // FirstWeight is non-null in this state by construction: nothing else sets
        // AwaitingSecondWeight.
        var gross = Mode == WeighmentMode.GrossFirst ? FirstWeight!.Kilograms : capture.Kilograms;
        var tare = Mode == WeighmentMode.GrossFirst ? capture.Kilograms : FirstWeight!.Kilograms;

        if (gross <= tare)
        {
            throw new InvalidOperationException(
                $"The gross weight ({gross:0.##} kg) must be greater than the tare weight ({tare:0.##} kg). " +
                "Check whether the vehicle arrived loaded or empty.");
        }

        SecondWeight = capture;
        NetWeightKg = gross - tare;
        Status = WeighmentStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Abandons the weighment, keeping the row and the reason.
    /// </summary>
    /// <exception cref="InvalidOperationException">The weighment is already finished.</exception>
    public void Cancel(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!IsOpen)
        {
            throw new InvalidOperationException(
                Status == WeighmentStatus.Completed
                    ? $"{Describe()} is complete and cannot be cancelled. Completed weighments are corrected by a new weighment, not by cancelling the record a slip was printed from."
                    : $"{Describe()} is already cancelled.");
        }

        Status = WeighmentStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        CancellationReason = Trim(reason);
    }

    /// <summary>Updates the details a weighment can still carry while it is open.</summary>
    /// <exception cref="InvalidOperationException">The weighment is finished.</exception>
    public void UpdateDetails(
        string? partyName,
        string? materialName,
        string? driverName,
        string? transporterName,
        string? remarks)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException($"{Describe()} is {Status} and can no longer be edited.");
        }

        PartyName = Trim(partyName);
        MaterialName = Trim(materialName);
        DriverName = Trim(driverName);
        TransporterName = Trim(transporterName);
        Remarks = Trim(remarks);
    }

    /// <summary>Attaches a captured image metadata record to this weighment.</summary>
    public void AttachImage(WeighmentImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        _images.Add(image);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Describe()} [{Status}]";

    /// <summary>Upper-cases a registration and removes the spaces and dashes operators vary on.</summary>
    public static string NormaliseVehicleNumber(string vehicleNumber)
    {
        ArgumentNullException.ThrowIfNull(vehicleNumber);

        return string.Concat(
            vehicleNumber.Where(character => !char.IsWhiteSpace(character) && character is not ('-' or '.')))
            .ToUpperInvariant();
    }

    private static void GuardWeight(decimal kilograms)
    {
        if (kilograms <= 0m || kilograms > MaximumWeightKg)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kilograms),
                kilograms,
                $"A weight must be greater than zero and no more than {MaximumWeightKg:0} kg.");
        }
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private string Describe()
        => string.IsNullOrEmpty(SlipNumber)
            ? $"the weighment for {VehicleNumber}"
            : $"weighment {SlipNumber} ({VehicleNumber})";
}
