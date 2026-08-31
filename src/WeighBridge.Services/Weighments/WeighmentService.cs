using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Events;
using WeighBridge.Core.Events.Catalog;
using WeighBridge.Core.Security;
using WeighBridge.Domain.Enums;
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
    private readonly ILogger<WeighmentService> _logger;

    public WeighmentService(
        Func<IUnitOfWork> unitOfWork,
        IPermissionService permissions,
        IEventPublisher events,
        ILogger<WeighmentService> logger,
        IOptions<WeighmentOptions>? options = null)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _permissions = permissions ?? throw new ArgumentNullException(nameof(permissions));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options;
    }

    /// <inheritdoc />
    public async Task<Weighment> CreateAsync(NewWeighment request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _permissions.Ensure(Permissions.WeighmentCreate);

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
            request.NumberOfBags,
            request.BagWeightKg,
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
            target => target.RecordFirstWeight(new WeightCapture(kilograms, DateTime.UtcNow, source)),
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
            target => target.UpdateSecondEntryDetails(
                request.SecondCharges,
                request.NumberOfBags,
                request.BagWeightKg,
                request.GatePassNumber,
                request.Remarks,
                request.CustomField3,
                request.CustomField4),
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

        var policy = (_options?.Value?.AllowZeroNetWeight == true)
            ? NetWeightPolicy.AllowZero
            : NetWeightPolicy.RejectZero;

        var weighment = await MutateAsync(
            request.WeighmentId,
            target =>
            {
                target.UpdateSecondEntryDetails(
                    request.SecondCharges,
                    request.NumberOfBags,
                    request.BagWeightKg,
                    request.GatePassNumber,
                    request.Remarks,
                    request.CustomField3,
                    request.CustomField4);

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
        Action<Weighment> transition,
        CancellationToken cancellationToken)
    {
        await using var unitOfWork = _unitOfWork();
        var repository = unitOfWork.Repository<Weighment>();

        var weighment = await repository.GetByIdAsync(weighmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Weighment {weighmentId} was not found. It may have been retired by an administrator.");

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
}