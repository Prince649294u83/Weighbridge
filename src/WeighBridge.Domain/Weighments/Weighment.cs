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

    /// <summary>Longest gate pass reference number the domain will accept.</summary>
    public const int GatePassMaxLength = 64;

    /// <summary>Longest custom field value the domain will accept.</summary>
    public const int CustomFieldMaxLength = 128;

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
    /// <param name="charges">Optional F1 weighing charges in rupees (must be non-negative).</param>
    /// <param name="numberOfBags">Optional count of packaging bags (must be non-negative).</param>
    /// <param name="bagWeightKg">Optional empty bag weight in kg (must be non-negative).</param>
    /// <param name="gatePassNumber">Optional gate pass reference number.</param>
    /// <param name="customField1">Optional F1 custom field 1 (locked in F2).</param>
    /// <param name="customField2">Optional F1 custom field 2 (locked in F2).</param>
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
        string? vehicleTypeName = null,
        decimal charges = 0m,
        int? numberOfBags = null,
        decimal? bagWeightKg = null,
        string? gatePassNumber = null,
        string? customField1 = null,
        string? customField2 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vehicleNumber);

        if (charges < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(charges), charges, "Charges cannot be negative.");
        }

        if (numberOfBags is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numberOfBags), numberOfBags, "Number of bags cannot be negative.");
        }

        if (bagWeightKg is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(bagWeightKg), bagWeightKg, "Bag weight cannot be negative.");
        }

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
            Charges = charges,
            NumberOfBags = numberOfBags,
            BagWeightKg = bagWeightKg,
            GatePassNumber = Trim(gatePassNumber),
            CustomField1 = Trim(customField1),
            CustomField2 = Trim(customField2),
            Version = Guid.NewGuid(),
        };
    }

    /// <summary>
    /// Opens a new weighment bound to a pre-allocated <see cref="TicketReservation"/>.
    /// </summary>
    /// <remarks>
    /// Enforces the exact same domain validation rules as <see cref="Open"/>, but sets the authoritative
    /// <see cref="SlipNumber"/> and <see cref="ReservationId"/> immediately upon creation.
    /// </remarks>
    public static Weighment OpenWithReservedSlip(
        string slipNumber,
        long reservationId,
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
        string? vehicleTypeName = null,
        decimal charges = 0m,
        int? numberOfBags = null,
        decimal? bagWeightKg = null,
        string? gatePassNumber = null,
        string? customField1 = null,
        string? customField2 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slipNumber);
        if (reservationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservationId), reservationId, "Reservation ID must be positive.");
        }

        if (!SlipNumbers.TryParse(slipNumber, out _))
        {
            throw new ArgumentException($"'{slipNumber}' is not a valid slip number format.", nameof(slipNumber));
        }

        var weighment = Open(
            vehicleNumber,
            mode,
            partyName,
            materialName,
            driverName,
            transporterName,
            remarks,
            vehicleId,
            partyId,
            materialId,
            vehicleTypeId,
            vehicleTypeName,
            charges,
            numberOfBags,
            bagWeightKg,
            gatePassNumber,
            customField1,
            customField2);

        weighment.SlipNumber = SlipNumbers.Normalise(slipNumber)!;
        weighment.ReservationId = reservationId;
        return weighment;
    }

    /// <summary>Human-readable slip number, for example <c>WB-000042</c>. Unique.</summary>
    /// <remarks>Empty only between the insert that allocates the identity and the update
    /// that derives this from it, inside one transaction.</remarks>
    public string SlipNumber { get; private set; } = string.Empty;

    /// <summary>Optional foreign key to the <see cref="TicketReservation"/> this weighment consumed.</summary>
    public long? ReservationId { get; private set; }

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

    /// <summary>First entry weighing fee/charges in rupees.</summary>
    public decimal Charges { get; private set; }

    /// <summary>Second entry weighing fee/charges in rupees.</summary>
    public decimal SecondCharges { get; private set; }

    /// <summary>Number of packaging bags deducted from cargo weight.</summary>
    public int? NumberOfBags { get; private set; }

    /// <summary>Tare weight per individual empty bag in kilograms.</summary>
    public decimal? BagWeightKg { get; private set; }

    /// <summary>Total calculated deduction for all packaging bags in kilograms.</summary>
    public decimal TotalBagWeightKg => (NumberOfBags ?? 0) * (BagWeightKg ?? 0m);

    /// <summary>
    /// Actual net material weight after deducting total bag tare weight (<c>NetWeightKg - TotalBagWeightKg</c>).
    /// Null until second weight is recorded.
    /// </summary>
    public decimal? ActualWeightKg => NetWeightKg.HasValue ? NetWeightKg.Value - TotalBagWeightKg : null;

    /// <summary>Gate pass reference or challan number.</summary>
    public string? GatePassNumber { get; private set; }

    /// <summary>Site-configurable custom field 1 (captured at F1, locked in F2).</summary>
    public string? CustomField1 { get; private set; }

    /// <summary>Site-configurable custom field 2 (captured at F1, locked in F2).</summary>
    public string? CustomField2 { get; private set; }

    /// <summary>Site-configurable custom field 3 (editable during F2).</summary>
    public string? CustomField3 { get; private set; }

    /// <summary>Site-configurable custom field 4 (editable during F2).</summary>
    public string? CustomField4 { get; private set; }

    /// <summary>
    /// Application concurrency token, regenerated on every material state transition.
    /// </summary>
    public Guid Version { get; private set; } = Guid.NewGuid();

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
        Version = Guid.NewGuid();
    }

    /// <summary>
    /// Overrides the weighment mode prior to second weight capture if the initial classification was incorrect.
    /// </summary>
    /// <param name="newMode">The updated weighment mode.</param>
    /// <exception cref="InvalidOperationException">Weighment is not in AwaitingSecondWeight status.</exception>
    public void OverrideMode(WeighmentMode newMode)
    {
        if (Status != WeighmentStatus.AwaitingSecondWeight)
        {
            throw new InvalidOperationException(
                $"Mode can only be overridden while awaiting second weight for {Describe()}. Current status is {Status}.");
        }

        Mode = newMode;
        Version = Guid.NewGuid();
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
    /// <param name="capture">Weight captured at second entry.</param>
    /// <param name="policy">Zero net weight policy (defaults to RejectZero).</param>
    /// <exception cref="InvalidOperationException">
    /// The weighment is not <see cref="WeighmentStatus.AwaitingSecondWeight"/>, or the two
    /// weights do not yield a valid net weight under the given policy, or bag deduction exceeds net weight.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The weight is not plausible.</exception>
    public void RecordSecondWeight(WeightCapture capture, NetWeightPolicy policy = NetWeightPolicy.RejectZero)
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

        if (gross < tare)
        {
            throw new InvalidOperationException(
                $"The gross weight ({gross:0.##} kg) must be greater than the tare weight ({tare:0.##} kg). " +
                "Check whether the vehicle arrived loaded or empty.");
        }

        if (gross == tare && policy != NetWeightPolicy.AllowZero)
        {
            throw new InvalidOperationException(
                $"The gross weight ({gross:0.##} kg) must be greater than the tare weight ({tare:0.##} kg). " +
                "The gross weight and tare weight are equal, resulting in zero net weight. Zero net weight is disallowed by policy.");
        }

        var net = gross - tare;
        var actualWeight = net - TotalBagWeightKg;

        if (actualWeight < 0m)
        {
            throw new InvalidOperationException(
                $"The total bag weight ({TotalBagWeightKg:0.##} kg) exceeds the net weight ({net:0.##} kg), " +
                $"resulting in a negative actual material weight ({actualWeight:0.##} kg).");
        }

        SecondWeight = capture;
        NetWeightKg = net;
        Status = WeighmentStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        Version = Guid.NewGuid();
    }

    /// <summary>
    /// Completes a newly opened weighment in one operator action using the live capture and a supplied tare.
    /// </summary>
    public void RecordSingleEntryWeight(
        WeightCapture capture,
        decimal tareWeightKg,
        NetWeightPolicy policy = NetWeightPolicy.RejectZero)
    {
        ArgumentNullException.ThrowIfNull(capture);
        GuardWeight(capture.Kilograms);
        GuardWeight(tareWeightKg);

        if (Status != WeighmentStatus.Created)
        {
            throw new InvalidOperationException(
                $"{Describe()} is {Status} and cannot be completed as a single-entry weighment.");
        }

        var gross = Mode == WeighmentMode.GrossFirst ? capture.Kilograms : tareWeightKg;
        var tare = Mode == WeighmentMode.GrossFirst ? tareWeightKg : capture.Kilograms;

        if (gross < tare)
        {
            throw new InvalidOperationException(
                $"The gross weight ({gross:0.##} kg) must be greater than the tare weight ({tare:0.##} kg). " +
                "Check whether the vehicle arrived loaded or empty.");
        }

        if (gross == tare && policy != NetWeightPolicy.AllowZero)
        {
            throw new InvalidOperationException(
                $"The gross weight ({gross:0.##} kg) must be greater than the tare weight ({tare:0.##} kg). " +
                "The gross weight and tare weight are equal, resulting in zero net weight. Zero net weight is disallowed by policy.");
        }

        var net = gross - tare;
        var actualWeight = net - TotalBagWeightKg;
        if (actualWeight < 0m)
        {
            throw new InvalidOperationException(
                $"The total bag weight ({TotalBagWeightKg:0.##} kg) exceeds the net weight ({net:0.##} kg), " +
                $"resulting in a negative actual material weight ({actualWeight:0.##} kg).");
        }

        FirstWeight = Mode == WeighmentMode.GrossFirst
            ? capture
            : new WeightCapture(tareWeightKg, capture.CapturedAtUtc, WeightSource.MasterTare);
        SecondWeight = Mode == WeighmentMode.GrossFirst
            ? new WeightCapture(tareWeightKg, capture.CapturedAtUtc, WeightSource.MasterTare)
            : capture;
        NetWeightKg = net;
        Status = WeighmentStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        Version = Guid.NewGuid();
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
        Version = Guid.NewGuid();
    }

    /// <summary>
    /// Updates the details an F1 weighment can carry while it is in <see cref="WeighmentStatus.Created"/>.
    /// Once the first weight is recorded, F1 historical details become permanently locked.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The weighment has already recorded its first weight or is finished.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">Charges or bag parameters are negative.</exception>
    public void UpdateDetails(
        string? partyName,
        string? materialName,
        string? driverName,
        string? transporterName,
        string? remarks,
        decimal charges = 0m,
        int? numberOfBags = null,
        decimal? bagWeightKg = null,
        string? gatePassNumber = null,
        string? customField1 = null,
        string? customField2 = null)
    {
        if (Status != WeighmentStatus.Created)
        {
            throw new InvalidOperationException(
                Status == WeighmentStatus.AwaitingSecondWeight
                    ? $"F1 historical details are locked once the first weight is recorded for {Describe()}."
                    : $"{Describe()} is {Status} and can no longer be edited.");
        }

        if (charges < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(charges), charges, "Charges cannot be negative.");
        }

        if (numberOfBags is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numberOfBags), numberOfBags, "Number of bags cannot be negative.");
        }

        if (bagWeightKg is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(bagWeightKg), bagWeightKg, "Bag weight cannot be negative.");
        }

        PartyName = Trim(partyName);
        MaterialName = Trim(materialName);
        DriverName = Trim(driverName);
        TransporterName = Trim(transporterName);
        Remarks = Trim(remarks);
        Charges = charges;
        NumberOfBags = numberOfBags;
        BagWeightKg = bagWeightKg;
        GatePassNumber = Trim(gatePassNumber);
        CustomField1 = Trim(customField1);
        CustomField2 = Trim(customField2);
        Version = Guid.NewGuid();
    }

    /// <summary>
    /// Updates dedicated second-entry details while the weighment is in <see cref="WeighmentStatus.AwaitingSecondWeight"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The weighment is not awaiting second weight.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Charges or bag parameters are negative.</exception>
    public void UpdateSecondEntryDetails(
        decimal secondCharges,
        int? numberOfBags,
        decimal? bagWeightKg,
        string? gatePassNumber,
        string? remarks,
        string? customField3 = null,
        string? customField4 = null)
    {
        if (Status != WeighmentStatus.AwaitingSecondWeight)
        {
            throw new InvalidOperationException(
                $"Second-entry details can only be updated while awaiting second weight. {Describe()} is {Status}.");
        }

        if (secondCharges < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(secondCharges), secondCharges, "Second charges cannot be negative.");
        }

        if (numberOfBags is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numberOfBags), numberOfBags, "Number of bags cannot be negative.");
        }

        if (bagWeightKg is < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(bagWeightKg), bagWeightKg, "Bag weight cannot be negative.");
        }

        SecondCharges = secondCharges;
        NumberOfBags = numberOfBags;
        BagWeightKg = bagWeightKg;
        GatePassNumber = Trim(gatePassNumber);
        Remarks = Trim(remarks);
        CustomField3 = Trim(customField3);
        CustomField4 = Trim(customField4);
        Version = Guid.NewGuid();
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
