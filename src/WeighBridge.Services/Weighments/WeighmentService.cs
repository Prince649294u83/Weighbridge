using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Messaging;
using WeighBridge.Core.Printing;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Masters;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Services.Weighments;

/// <summary>
/// The weighment business service: one transaction and one published fact per call.
/// </summary>
/// <remarks>
/// <para>
/// Takes a <see cref="Func{IUnitOfWork}"/> rather than an <see cref="IUnitOfWork"/>. The
/// unit of work is registered transient because it owns a <c>DbContext</c> and its change
/// tracker; a singleton service that captured one would hold the same tracker — and the
/// same open connection — for the weeks this application stays running, accumulating every
/// row any operator ever looked at. The factory hands out a fresh one per operation and the
/// <c>await using</c> disposes it.
/// </para>
/// <para>
/// Events are published after the transaction commits, never before. A subscriber that
/// printed a slip or advanced a barrier for a weighment that then failed to save would be
/// acting on something that did not happen.
/// </para>
/// <para>
/// Business rules the operator can fix live in the validators the commands run. What is
/// enforced here is only what the aggregate itself refuses — the service does not re-check
/// an invariant the domain already owns, because two copies of a rule are one rule and one
/// bug waiting for them to disagree.
/// </para>
/// </remarks>
public sealed class WeighmentService : IWeighmentService
{
    private const string ModuleName = "VehicleEntry";

    private readonly Func<IUnitOfWork> _unitOfWork;
    private readonly IPermissionService _permissions;
    private readonly IEventPublisher _events;
    private readonly IOptions<WeighmentOptions>? _options;
    private readonly IOptionsMonitor<WeighmentOptions>? _optionsMonitor;
    private readonly ILogger<WeighmentService> _logger;
    private readonly IEmailService? _emailService;
    private readonly IOptionsMonitor<EmailOptions>? _emailOptionsMonitor;
    private readonly ISmsService? _smsService;
    private readonly IOptionsMonitor<SmsOptions>? _smsOptionsMonitor;

    public WeighmentService(
        Func<IUnitOfWork> unitOfWork,
        IPermissionService permissions,
        IEventPublisher events,
        ILogger<WeighmentService> logger,
        IOptions<WeighmentOptions>? options = null,
        IOptionsMonitor<WeighmentOptions>? optionsMonitor = null,
        IEmailService? emailService = null,
        IOptionsMonitor<EmailOptions>? emailOptionsMonitor = null,
        ISmsService? smsService = null,
        IOptionsMonitor<SmsOptions>? smsOptionsMonitor = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options;
        _optionsMonitor = optionsMonitor;
        _emailService = emailService;
        _emailOptionsMonitor = emailOptionsMonitor;
        _smsService = smsService;
        _smsOptionsMonitor = smsOptionsMonitor;
    }

    /// <inheritdoc />
    public async Task<Weighment> CreateAsync(NewWeighment request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _permissions.Ensure(Permissions.WeighmentCreate);

        var options = CurrentOptions;
        EnsureChargesAllowed(request.Charges, nameof(request.Charges), options);

        var weighment = Weighment.Open(
            request.VehicleNumber,
            request.Mode,
            request.PartyName,
            request.MaterialName,
            request.DriverName,
            request.TransporterName,
            request.Remarks,
            request.VehicleId,
            request.PartyId,
            request.MaterialId,
            request.VehicleTypeId,
            request.VehicleTypeName,
            request.Charges,
            options.UnitBagsWeightColumn ? request.NumberOfBags : null,
            options.UnitBagsWeightColumn ? request.BagWeightKg : null,
            request.GatePassNumber,
            request.CustomField1,
            request.CustomField2);

        weighment.CreatedBy = CurrentOperatorName;

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Weighment>();

        var canonical = Weighment.NormaliseVehicleNumber(request.VehicleNumber);
        var pending = await repository.FindAsync(
            w => w.VehicleNumber == canonical &&
                 (w.Status == WeighmentStatus.Created || w.Status == WeighmentStatus.AwaitingSecondWeight),
            cancellationToken).ConfigureAwait(false);

        if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"Vehicle {request.VehicleNumber} already has an open weighment ({pending[0].SlipNumber}). Complete or cancel it before opening a new one.");
        }

        // Two saves in one transaction: the first allocates the identity, the second stores
        // the slip number derived from it. Either both land or neither does, so a weighment
        // without a slip number can never be observed. The database's partial unique index
        // on open vehicle numbers backs the pre-check above: if another terminal opened
        // this vehicle between the check and the insert, SQLite refuses and the operator
        // gets the same friendly message rather than a raw constraint error.
        await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await repository.AddAsync(weighment, cancellationToken).ConfigureAwait(false);
            await WeighmentSaveAsync(unitOfWork, request.VehicleNumber, cancellationToken).ConfigureAwait(false);

            weighment.AssignSlipNumber();

            // Already tracked, so the change tracker picks the slip number up on its own.
            // Calling Update() here would stamp ModifiedAtUtc and make every brand-new
            // weighment claim to have been edited.
            await unitOfWork.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation(
            "Weighment {SlipNumber} opened for {VehicleNumber} in {Mode} mode by {Operator}",
            weighment.SlipNumber,
            weighment.VehicleNumber,
            weighment.Mode,
            weighment.CreatedBy);

        _events.Publish(new WeighmentCreatedEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            weighment.Mode,
            ModuleName));

        return weighment;
    }

    /// <inheritdoc />
    public async Task<Weighment> RecordFirstWeightAsync(
        long weighmentId,
        decimal kilograms,
        WeightSource source,
        CancellationToken cancellationToken = default)
    {
        _permissions.Ensure(Permissions.WeighmentCreate);

        var weighment = await MutateAsync(
            weighmentId,
            expectedVersion: null,
            target =>
            {
                EnsureWeightSourceAllowed(source, CurrentOptions);
                target.RecordFirstWeight(new WeightCapture(kilograms, DateTime.UtcNow, source));
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "First weight {Kilograms} kg ({Source}) recorded on {SlipNumber} for {VehicleNumber}",
            kilograms,
            source,
            weighment.SlipNumber,
            weighment.VehicleNumber);

        _events.Publish(new FirstWeightRecordedEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            kilograms,
            source,
            ModuleName));

        TrySendWeighmentEmail(weighment, isFirstEntry: true);

        return weighment;
    }

    /// <inheritdoc />
    public async Task<Weighment> UpdateSecondEntryDetailsAsync(
        UpdateSecondEntryDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _permissions.Ensure(Permissions.WeighmentEdit);

        var weighment = await MutateAsync(
            request.WeighmentId,
            request.ExpectedVersion,
            target =>
            {
                var options = CurrentOptions;
                target.UpdateSecondEntryDetails(
                    options.SecondEntryCharges ? request.SecondCharges : 0m,
                    options.UnitBagsWeightColumn ? request.NumberOfBags : null,
                    options.UnitBagsWeightColumn ? request.BagWeightKg : null,
                    request.GatePassNumber,
                    request.Remarks,
                    request.CustomField3,
                    request.CustomField4);
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Second-entry details updated for {SlipNumber} ({VehicleNumber}) by {Operator}",
            weighment.SlipNumber,
            weighment.VehicleNumber,
            weighment.ModifiedBy);

        return weighment;
    }

    /// <inheritdoc />
    public async Task<Weighment> RecordSecondWeightAsync(
        RecordSecondWeightRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _permissions.Ensure(Permissions.WeighmentEdit);

        var options = CurrentOptions;
        var policy = options.AllowZeroNetWeight
            ? NetWeightPolicy.AllowZero
            : NetWeightPolicy.RejectZero;

        var weighment = await MutateAsync(
            request.WeighmentId,
            request.ExpectedVersion,
            target =>
            {
                EnsureWeightSourceAllowed(request.Source, options);
                var secondCharges = options.SecondEntryCharges ? request.SecondCharges : 0m;
                var totalCharges = target.Charges + secondCharges;
                EnsureChargesAllowed(totalCharges, nameof(request.SecondCharges), options);

                target.UpdateSecondEntryDetails(
                    secondCharges,
                    options.UnitBagsWeightColumn ? request.NumberOfBags : null,
                    options.UnitBagsWeightColumn ? request.BagWeightKg : null,
                    request.GatePassNumber,
                    request.Remarks,
                    request.CustomField3,
                    request.CustomField4);

                if (request.ModeOverride.HasValue && request.ModeOverride.Value != target.Mode)
                {
                    target.OverrideMode(request.ModeOverride.Value);
                }

                target.RecordSecondWeight(
                    new WeightCapture(request.Kilograms, DateTime.UtcNow, request.Source),
                    policy);
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Second weight {Kilograms} kg ({Source}) recorded on {SlipNumber}; net {Net} kg (Actual: {Actual} kg)",
            request.Kilograms,
            request.Source,
            weighment.SlipNumber,
            weighment.NetWeightKg,
            weighment.ActualWeightKg);

        _events.Publish(new SecondWeightRecordedEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            request.Kilograms,
            request.Source,
            ModuleName));

        // Two events for one call, because they are two facts. A yard display cares that a
        // vehicle was weighed; the printer and the reports care that a transaction closed.
        _events.Publish(new WeighmentCompletedEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            weighment.Gross!.Kilograms,
            weighment.Tare!.Kilograms,
            weighment.NetWeightKg!.Value,
            ModuleName));

        TrySendWeighmentEmail(weighment, isFirstEntry: false);
        TrySendWeighmentSms(weighment);

        if (options.AutoUpdateTareWeight && weighment.Tare is not null)
        {
            await TryUpdateVehicleTareWeightAsync(weighment.VehicleId, weighment.VehicleNumber, weighment.Tare.Kilograms, cancellationToken).ConfigureAwait(false);
        }

        return weighment;
    }

    /// <inheritdoc />
    public Task<Weighment> RecordSecondWeightAsync(
        long weighmentId,
        decimal kilograms,
        WeightSource source,
        CancellationToken cancellationToken = default)
        => RecordSecondWeightAsync(
            new RecordSecondWeightRequest(weighmentId, kilograms, source),
            cancellationToken);

    /// <inheritdoc />
    public async Task<Weighment> RecordSingleEntryWeightAsync(
        RecordSingleEntryWeightRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _permissions.Ensure(Permissions.WeighmentCreate);

        var options = CurrentOptions;
        if (!options.OnlySingleEntry)
        {
            throw new InvalidOperationException("Single-entry completion is disabled by site settings.");
        }

        if (!options.AutoTareWeight)
        {
            throw new InvalidOperationException("Single-entry completion requires Auto Tare Weight to be enabled.");
        }

        var policy = options.AllowZeroNetWeight
            ? NetWeightPolicy.AllowZero
            : NetWeightPolicy.RejectZero;

        var weighment = await MutateAsync(
            request.WeighmentId,
            request.ExpectedVersion,
            target =>
            {
                EnsureWeightSourceAllowed(request.Source, options);
                EnsureChargesAllowed(target.Charges, nameof(target.Charges), options);
                target.RecordSingleEntryWeight(
                    new WeightCapture(request.Kilograms, DateTime.UtcNow, request.Source),
                    request.TareWeightKg,
                    policy);
            },
            cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Single-entry weight {Kilograms} kg ({Source}) completed {SlipNumber}; net {Net} kg",
            request.Kilograms,
            request.Source,
            weighment.SlipNumber,
            weighment.NetWeightKg);

        _events.Publish(new FirstWeightRecordedEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            request.Kilograms,
            request.Source,
            ModuleName));

        _events.Publish(new WeighmentCompletedEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            weighment.Gross!.Kilograms,
            weighment.Tare!.Kilograms,
            weighment.NetWeightKg!.Value,
            ModuleName));

        TrySendWeighmentEmail(weighment, isFirstEntry: false);
        TrySendWeighmentSms(weighment);

        if (options.AutoUpdateTareWeight && weighment.Tare is not null)
        {
            await TryUpdateVehicleTareWeightAsync(weighment.VehicleId, weighment.VehicleNumber, weighment.Tare.Kilograms, cancellationToken).ConfigureAwait(false);
        }

        return weighment;
    }

    /// <inheritdoc />
    public async Task<Weighment?> FindPendingSecondEntryAsync(
        string searchKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchKey))
        {
            return null;
        }

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Weighment>();

        // Canonical or human-entered ticket number resolution (e.g. WB-000001, 1, 000001, wb-1)
        if (SlipNumbers.Normalise(searchKey) is { } canonicalSlip)
        {
            var matches = await repository.FindAsync(
                w => w.SlipNumber == canonicalSlip && w.Status == WeighmentStatus.AwaitingSecondWeight,
                cancellationToken).ConfigureAwait(false);

            if (matches.Count > 0)
            {
                return matches[0];
            }
        }

        // Canonical vehicle registration resolution
        var canonicalVehicle = Weighment.NormaliseVehicleNumber(searchKey);
        if (string.IsNullOrWhiteSpace(canonicalVehicle))
        {
            return null;
        }

        var vehicleMatches = await repository.FindAsync(
            w => w.VehicleNumber == canonicalVehicle && w.Status == WeighmentStatus.AwaitingSecondWeight,
            cancellationToken).ConfigureAwait(false);

        if (vehicleMatches.Count == 0)
        {
            return null;
        }

        if (vehicleMatches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple pending transactions found for vehicle '{searchKey}'. Please select from the waiting list or enter the exact ticket number.");
        }

        return vehicleMatches[0];
    }

    /// <inheritdoc />
    public async Task<Weighment> CancelAsync(
        long weighmentId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        _permissions.Ensure(Permissions.WeighmentCancel);

        var weighment = await MutateAsync(
            weighmentId,
            expectedVersion: null,
            target => target.Cancel(reason),
            cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Weighment {SlipNumber} for {VehicleNumber} cancelled by {Operator}: {Reason}",
            weighment.SlipNumber,
            weighment.VehicleNumber,
            weighment.ModifiedBy,
            reason);

        _events.Publish(new WeighmentCancelledEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            reason,
            ModuleName));

        return weighment;
    }

    /// <inheritdoc />
    public async Task<Weighment?> GetAsync(long weighmentId, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();

        return await unitOfWork.Repository<Weighment>()
            .GetByIdAsync(weighmentId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Weighment?> GetBySlipNumberAsync(
        string slipNumber,
        CancellationToken cancellationToken = default)
    {
        // A slip number an operator reads off paper arrives with whatever spacing and case
        // they used. Canonicalise before querying rather than widening the query.
        if (SlipNumbers.Normalise(slipNumber) is not { } canonical)
        {
            return null;
        }

        await using var unitOfWork = _unitOfWork();

        var matches = await unitOfWork.Repository<Weighment>()
            .FindAsync(weighment => weighment.SlipNumber == canonical, cancellationToken)
            .ConfigureAwait(false);

        return matches.Count == 0 ? null : matches[0];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Weighment>> GetAwaitingSecondWeightAsync(
        CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();

        return await unitOfWork.Repository<Weighment>()
            .ListRecentAsync(
                weighment => weighment.Status == WeighmentStatus.AwaitingSecondWeight,
                take: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Weighment>> GetRecentAsync(
        int count = 20,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Ask for at least one weighment.");
        }

        await using var unitOfWork = _unitOfWork();

        return await unitOfWork.Repository<Weighment>()
            .ListRecentAsync(predicate: null, take: count, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<WeighmentImage> AttachImageAsync(
        long weighmentId,
        string cameraName,
        string stage,
        CameraSource source,
        string relativeFilePath,
        DateTime capturedAtUtc,
        long fileSizeBytes,
        string? sha256Checksum = null,
        CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        var weighmentRepo = unitOfWork.Repository<Weighment>();
        var weighment = await weighmentRepo.GetByIdAsync(weighmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Weighment {weighmentId} was not found.");

        var image = WeighmentImage.Create(
            weighmentId,
            cameraName,
            stage,
            source,
            relativeFilePath,
            capturedAtUtc,
            fileSizeBytes,
            sha256Checksum);

        image.CreatedBy = CurrentOperatorName;
        weighment.AttachImage(image);

        await unitOfWork.Repository<WeighmentImage>().AddAsync(image, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Attached {CameraName} snapshot ({Stage}) to {SlipNumber} -> {FilePath}",
            cameraName,
            stage,
            weighment.SlipNumber,
            relativeFilePath);

        return image;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WeighmentImage>> GetImagesAsync(long weighmentId, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return await unitOfWork.Repository<WeighmentImage>()
            .ListRecentAsync(img => img.WeighmentId == weighmentId, take: null, cancellationToken)
            .ConfigureAwait(false);
    }

    private string CurrentOperatorName => _permissions.CurrentOperator.UserName;

    private WeighmentOptions CurrentOptions => _optionsMonitor?.CurrentValue ?? _options?.Value ?? new WeighmentOptions();

    private static void EnsureWeightSourceAllowed(WeightSource source, WeighmentOptions options)
    {
        if (source == WeightSource.Manual && !options.ManualTareEntry)
        {
            throw new InvalidOperationException("Manual weight entry is disabled by site settings. Capture the weight from the indicator.");
        }
    }

    private static void EnsureChargesAllowed(decimal charges, string propertyName, WeighmentOptions options)
    {
        if (options.ChargesMandatory && charges <= 0m)
        {
            throw new InvalidOperationException("Weighing charges are mandatory according to site settings.");
        }

        if (options.MinimumCharges > 0m && charges < options.MinimumCharges)
        {
            throw new InvalidOperationException(
                $"Weighing charges must be at least {options.MinimumCharges:0.##}.");
        }
    }

    /// <summary>
    /// Loads a weighment, applies a transition to it and saves.
    /// </summary>
    /// <remarks>
    /// The transition itself is the aggregate's; this only supplies the transaction and the
    /// audit stamp. An <see cref="InvalidOperationException"/> out of
    /// <paramref name="transition"/> reaches the caller unchanged — the command pipeline
    /// turns it into a failed result with the domain's own message, which is written for the
    /// operator, so wrapping it here would replace a useful sentence with a generic one.
    /// </remarks>
    private async Task<Weighment> MutateAsync(
        long weighmentId,
        Guid? expectedVersion,
        Action<Weighment> transition,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Weighment>();

        var weighment = await repository.GetByIdAsync(weighmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Weighment {weighmentId} was not found. It may have been retired by an administrator.");

        if (expectedVersion.HasValue && weighment.Version != expectedVersion.Value)
        {
            throw new InvalidOperationException(
                $"Weighment {weighment.SlipNumber ?? weighmentId.ToString()} was modified by another operator or process. Please reload the transaction before making changes.");
        }

        transition(weighment);

        weighment.ModifiedBy = CurrentOperatorName;
        repository.Update(weighment);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict saving weighment {Id}", weighmentId);
            throw new InvalidOperationException(
                $"Weighment {weighment.SlipNumber ?? weighmentId.ToString()} was modified by another operator or process. Please reload the transaction before making changes.",
                ex);
        }

        return weighment;
    }

    /// <inheritdoc />
    public async Task<TicketReservation> ReserveTicketAsync(
        string? tentativeVehicleNumber = null,
        string? terminalId = null,
        CancellationToken cancellationToken = default)
    {
        _permissions.Ensure(Permissions.WeighmentCreate);

        var reservation = TicketReservation.Create(
            tentativeVehicleNumber,
            terminalId,
            CurrentOperatorName);

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<TicketReservation>();

        await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await repository.AddAsync(reservation, cancellationToken).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            reservation.AssignSlipNumber();
            await unitOfWork.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation(
            "Ticket reservation {SlipNumber} created by {Operator} for terminal {TerminalId}",
            reservation.SlipNumber,
            reservation.CreatedBy,
            reservation.TerminalId);

        return reservation;
    }

    /// <inheritdoc />
    public async Task<TicketReservation?> GetReservationAsync(long reservationId, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        return await unitOfWork.Repository<TicketReservation>()
            .GetByIdAsync(reservationId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TicketReservation?> GetReservationBySlipNumberAsync(string slipNumber, CancellationToken cancellationToken = default)
    {
        var canonical = SlipNumbers.Normalise(slipNumber);
        if (canonical is null)
        {
            return null;
        }

        await using var unitOfWork = _unitOfWork();
        var found = await unitOfWork.Repository<TicketReservation>()
            .FindAsync(r => r.SlipNumber == canonical, cancellationToken)
            .ConfigureAwait(false);

        return found.Count > 0 ? found[0] : null;
    }

    /// <inheritdoc />
    public async Task<TicketReservation?> GetActiveReservationAsync(string? terminalId = null, CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<TicketReservation>();

        var reservations = await repository.ListRecentAsync(
            r => r.Status == TicketReservationStatus.Reserved &&
                 (terminalId == null || r.TerminalId == terminalId),
            take: 1,
            cancellationToken).ConfigureAwait(false);

        return reservations.Count > 0 ? reservations[0] : null;
    }

    /// <inheritdoc />
    public async Task<TicketReservation> CancelReservationAsync(
        long reservationId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        _permissions.Ensure(Permissions.WeighmentCreate);

        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<TicketReservation>();

        var reservation = await repository.GetByIdAsync(reservationId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Ticket reservation {reservationId} was not found.");

        reservation.Cancel(reason, CurrentOperatorName);
        repository.Update(reservation);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict cancelling reservation {Id}", reservationId);
            throw new InvalidOperationException(
                $"Reservation {reservation.SlipNumber} was modified by another operator or process.", ex);
        }

        _logger.LogInformation(
            "Ticket reservation {SlipNumber} cancelled by {Operator}. Reason: {Reason}",
            reservation.SlipNumber,
            reservation.ModifiedBy,
            reservation.CancellationReason);

        return reservation;
    }

    /// <inheritdoc />
    public async Task<Weighment> CreateWithReservationAndRecordFirstWeightAsync(
        long reservationId,
        NewWeighment request,
        decimal kilograms,
        WeightSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _permissions.Ensure(Permissions.WeighmentCreate);

        if (reservationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservationId), reservationId, "Reservation ID must be positive.");
        }

        var options = CurrentOptions;
        EnsureChargesAllowed(request.Charges, nameof(request.Charges), options);

        await using var unitOfWork = _unitOfWork();
        var reservationRepo = unitOfWork.Repository<TicketReservation>();
        var weighmentRepo = unitOfWork.Repository<Weighment>();

        var canonical = Weighment.NormaliseVehicleNumber(request.VehicleNumber);
        var pending = await weighmentRepo.FindAsync(
            w => w.VehicleNumber == canonical &&
                 (w.Status == WeighmentStatus.Created || w.Status == WeighmentStatus.AwaitingSecondWeight),
            cancellationToken).ConfigureAwait(false);

        if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"Vehicle {request.VehicleNumber} already has an open weighment ({pending[0].SlipNumber}). Complete or cancel it before opening a new one.");
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Weighment weighment;
        try
        {
            var reservation = await reservationRepo.GetByIdAsync(reservationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Ticket reservation {reservationId} was not found.");

            if (reservation.Status != TicketReservationStatus.Reserved)
            {
                throw new InvalidOperationException(
                    $"Ticket reservation {reservation.SlipNumber} is in '{reservation.Status}' status and cannot be consumed.");
            }

            weighment = Weighment.OpenWithReservedSlip(
                reservation.SlipNumber,
                reservation.Id,
                request.VehicleNumber,
                request.Mode,
                request.PartyName,
                request.MaterialName,
                request.DriverName,
                request.TransporterName,
                request.Remarks,
                request.VehicleId,
                request.PartyId,
                request.MaterialId,
                request.VehicleTypeId,
                request.VehicleTypeName,
                request.Charges,
                options.UnitBagsWeightColumn ? request.NumberOfBags : null,
                options.UnitBagsWeightColumn ? request.BagWeightKg : null,
                request.GatePassNumber,
                request.CustomField1,
                request.CustomField2);

            weighment.CreatedBy = CurrentOperatorName;

            var capture = new WeightCapture(kilograms, DateTime.UtcNow, source);
            weighment.RecordFirstWeight(capture);

            await weighmentRepo.AddAsync(weighment, cancellationToken).ConfigureAwait(false);
            await WeighmentSaveAsync(unitOfWork, request.VehicleNumber, cancellationToken).ConfigureAwait(false);

            reservation.Consume(weighment.Id, CurrentOperatorName);

            await unitOfWork.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation(
            "Weighment {SlipNumber} created and first weight {Weight} kg recorded for {VehicleNumber} in {Mode} mode by {Operator}",
            weighment.SlipNumber,
            kilograms,
            weighment.VehicleNumber,
            weighment.Mode,
            weighment.CreatedBy);

        _events.Publish(new WeighmentCreatedEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            weighment.Mode,
            ModuleName));

        _events.Publish(new FirstWeightRecordedEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            kilograms,
            source,
            ModuleName));

        return weighment;
    }

    /// <inheritdoc />
    public async Task<Weighment> CreateWithReservationAndRecordSingleEntryWeightAsync(
        long reservationId,
        NewWeighment request,
        decimal kilograms,
        WeightSource source,
        decimal tareWeightKg,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _permissions.Ensure(Permissions.WeighmentCreate);

        if (reservationId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reservationId), reservationId, "Reservation ID must be positive.");
        }

        var options = CurrentOptions;
        if (!options.OnlySingleEntry && !options.ManualTareEntry && !options.AutoTareWeight)
        {
            throw new InvalidOperationException("Single-entry weighments are disabled in site settings.");
        }

        EnsureChargesAllowed(request.Charges, nameof(request.Charges), options);

        await using var unitOfWork = _unitOfWork();
        var reservationRepo = unitOfWork.Repository<TicketReservation>();
        var weighmentRepo = unitOfWork.Repository<Weighment>();

        var canonical = Weighment.NormaliseVehicleNumber(request.VehicleNumber);
        var pending = await weighmentRepo.FindAsync(
            w => w.VehicleNumber == canonical &&
                 (w.Status == WeighmentStatus.Created || w.Status == WeighmentStatus.AwaitingSecondWeight),
            cancellationToken).ConfigureAwait(false);

        if (pending.Count > 0)
        {
            throw new InvalidOperationException(
                $"Vehicle {request.VehicleNumber} already has an open weighment ({pending[0].SlipNumber}). Complete or cancel it before opening a new one.");
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        Weighment weighment;
        try
        {
            var reservation = await reservationRepo.GetByIdAsync(reservationId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Ticket reservation {reservationId} was not found.");

            if (reservation.Status != TicketReservationStatus.Reserved)
            {
                throw new InvalidOperationException(
                    $"Ticket reservation {reservation.SlipNumber} is in '{reservation.Status}' status and cannot be consumed.");
            }

            weighment = Weighment.OpenWithReservedSlip(
                reservation.SlipNumber,
                reservation.Id,
                request.VehicleNumber,
                request.Mode,
                request.PartyName,
                request.MaterialName,
                request.DriverName,
                request.TransporterName,
                request.Remarks,
                request.VehicleId,
                request.PartyId,
                request.MaterialId,
                request.VehicleTypeId,
                request.VehicleTypeName,
                request.Charges,
                options.UnitBagsWeightColumn ? request.NumberOfBags : null,
                options.UnitBagsWeightColumn ? request.BagWeightKg : null,
                request.GatePassNumber,
                request.CustomField1,
                request.CustomField2);

            weighment.CreatedBy = CurrentOperatorName;

            var liveCapture = new WeightCapture(kilograms, DateTime.UtcNow, source);
            weighment.RecordSingleEntryWeight(liveCapture, tareWeightKg);

            await weighmentRepo.AddAsync(weighment, cancellationToken).ConfigureAwait(false);
            await WeighmentSaveAsync(unitOfWork, request.VehicleNumber, cancellationToken).ConfigureAwait(false);

            reservation.Consume(weighment.Id, CurrentOperatorName);

            await unitOfWork.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        _logger.LogInformation(
            "Single-entry weight {Kilograms} kg ({Source}) completed {SlipNumber}; net {Net} kg",
            kilograms,
            source,
            weighment.SlipNumber,
            weighment.NetWeightKg);

        _events.Publish(new FirstWeightRecordedEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            kilograms,
            source,
            ModuleName));

        _events.Publish(new WeighmentCompletedEvent(
            weighment.Id,
            weighment.SlipNumber,
            weighment.VehicleNumber,
            weighment.Gross!.Kilograms,
            weighment.Tare!.Kilograms,
            weighment.NetWeightKg!.Value,
            ModuleName));

        TrySendWeighmentEmail(weighment, isFirstEntry: false);
        TrySendWeighmentSms(weighment);

        if (options.AutoUpdateTareWeight && weighment.Tare is not null)
        {
            await TryUpdateVehicleTareWeightAsync(weighment.VehicleId, weighment.VehicleNumber, weighment.Tare.Kilograms, cancellationToken).ConfigureAwait(false);
        }

        return weighment;
    }

    /// <summary>
    /// Saves the open-weighment insert, translating the database's unique-index refusal
    /// (two terminals racing to open the same vehicle) into the operator-facing message
    /// the pre-check would have produced.
    /// </summary>
    private static async Task WeighmentSaveAsync(
        IUnitOfWork unitOfWork,
        string vehicleNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException(
                $"Vehicle {vehicleNumber} already has an open weighment. Complete or cancel it before opening a new one.",
                exception);
        }
    }

    private void TrySendWeighmentEmail(Weighment weighment, bool isFirstEntry)
    {
        if (_emailService == null || _emailOptionsMonitor == null)
        {
            return;
        }

        var emailConfig = _emailOptionsMonitor.CurrentValue;
        if (!emailConfig.Enabled || emailConfig.Recipients.Length == 0)
        {
            return;
        }

        var freq = emailConfig.Frequency ?? string.Empty;
        bool shouldSend = freq.Contains("Both", StringComparison.OrdinalIgnoreCase)
            || (!isFirstEntry && freq.Contains("Final", StringComparison.OrdinalIgnoreCase));

        if (!shouldSend)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var entryType = isFirstEntry ? "First Entry" : "Completed Weighment";
                var subject = $"Weighment Notification [{weighment.SlipNumber}] - {weighment.VehicleNumber}";
                var body = $"Weighbridge Alert: {entryType}\n" +
                           $"Slip Number: {weighment.SlipNumber}\n" +
                           $"Vehicle: {weighment.VehicleNumber}\n" +
                           $"Party: {weighment.PartyName}\n" +
                           $"Material: {weighment.MaterialName}\n" +
                           $"Gross Weight: {weighment.Gross?.Kilograms ?? 0} kg\n" +
                           $"Tare Weight: {weighment.Tare?.Kilograms ?? 0} kg\n" +
                           $"Net Weight: {weighment.NetWeightKg ?? 0} kg\n" +
                           $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n";

                await _emailService.SendEmailAsync(subject, body, emailConfig.Recipients).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background email dispatch failed for slip {SlipNumber}", weighment.SlipNumber);
            }
        });
    }

    private void TrySendWeighmentSms(Weighment weighment)
    {
        if (_smsService == null || _smsOptionsMonitor == null)
        {
            return;
        }

        var smsConfig = _smsOptionsMonitor.CurrentValue;
        if (!smsConfig.Enabled)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var printData = WeighmentPrintDataFactory.Create(weighment);
                await _smsService.QueueWeighmentSmsAsync(printData).ConfigureAwait(false);
                await _smsService.ProcessOutboxAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background SMS dispatch failed for slip {SlipNumber}", weighment.SlipNumber);
            }
        });
    }

    private async Task TryUpdateVehicleTareWeightAsync(
        long? vehicleId,
        string vehicleNumber,
        decimal tareWeightKg,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var unitOfWork = _unitOfWork();
            var vehicleRepo = unitOfWork.Repository<Vehicle>();
            var vehicle = vehicleId.HasValue
                ? await vehicleRepo.GetByIdAsync(vehicleId.Value, cancellationToken).ConfigureAwait(false)
                : (await vehicleRepo.FindAsync(v => v.VehicleNumber == vehicleNumber, cancellationToken).ConfigureAwait(false)).FirstOrDefault();

            if (vehicle is not null)
            {
                vehicle.UpdateTareWeight(tareWeightKg);
                vehicleRepo.Update(vehicle);
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Auto-updated Vehicle {Vehicle} tare weight to {Tare} kg", vehicle.VehicleNumber, tareWeightKg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-update tare weight for vehicle {VehicleNumber}", vehicleNumber);
        }
    }
}
