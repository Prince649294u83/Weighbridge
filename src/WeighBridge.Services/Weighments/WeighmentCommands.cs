using WeighBridge.Core.Abstractions;
using WeighBridge.Core.Commands;
using WeighBridge.Core.Security;
using WeighBridge.Core.Validation;
using WeighBridge.Domain.Enums;
using WeighBridge.Domain.Weighments;

namespace WeighBridge.Services.Weighments;

/// <summary>
/// Opens a weighment and allocates its slip number.
/// </summary>
/// <remarks>
/// Produces the saved <see cref="Weighment"/> because the screen has to show the slip number
/// the database just allocated, and reading it back out of the command afterwards would work
/// only for a command that kept it.
/// </remarks>
public sealed class CreateWeighmentCommand(IWeighmentService weighments, NewWeighment request)
    : IApplicationCommand<Weighment>, IValidatable, IRequiresPermission
{
    // Stateless and dependency-free, so one instance serves every execution.
    private static readonly NewWeighmentValidator Rules = new();

    private readonly IWeighmentService _weighments = weighments
        ?? throw new ArgumentNullException(nameof(weighments));

    private readonly NewWeighment _request = request ?? throw new ArgumentNullException(nameof(request));

    /// <inheritdoc />
    public string Name => "Open weighment";

    /// <inheritdoc />
    public Permission? RequiredPermission => Permissions.WeighmentCreate;

    /// <inheritdoc />
    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        => Rules.ValidateAsync(_request, cancellationToken);

    /// <inheritdoc />
    public async Task<CommandResult<Weighment>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ReportStatus("Opening weighment…");

        var weighment = await _weighments.CreateAsync(_request, context.CancellationToken).ConfigureAwait(false);

        context.Audit("SlipNumber", weighment.SlipNumber)
            .Audit("VehicleNumber", weighment.VehicleNumber)
            .Audit("Mode", weighment.Mode.ToString());

        return CommandResult<Weighment>.Success(
            weighment,
            $"Weighment {weighment.SlipNumber} opened for {weighment.VehicleNumber}.");
    }

    /// <inheritdoc />
    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

/// <summary>
/// Allocates an authoritative persistent ticket reservation in SQLite.
/// </summary>
public sealed class ReserveTicketCommand(
    IWeighmentService weighments,
    string? tentativeVehicleNumber = null,
    string? terminalId = null) : IApplicationCommand<TicketReservation>, IRequiresPermission
{
    private readonly IWeighmentService _weighments = weighments
        ?? throw new ArgumentNullException(nameof(weighments));

    /// <inheritdoc />
    public string Name => "Reserve ticket";

    /// <inheritdoc />
    public Permission? RequiredPermission => Permissions.WeighmentCreate;

    /// <inheritdoc />
    public async Task<CommandResult<TicketReservation>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ReportStatus("Allocating ticket number…");

        var reservation = await _weighments
            .ReserveTicketAsync(tentativeVehicleNumber, terminalId, context.CancellationToken)
            .ConfigureAwait(false);

        context.Audit("SlipNumber", reservation.SlipNumber)
            .Audit("ReservationId", reservation.Id.ToString())
            .Audit("Status", reservation.Status.ToString());

        return CommandResult<TicketReservation>.Success(
            reservation,
            $"Ticket {reservation.SlipNumber} reserved.");
    }

    /// <inheritdoc />
    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

/// <summary>
/// Cancels an unconsumed ticket reservation with an audited reason.
/// </summary>
public sealed class CancelReservationCommand(
    IWeighmentService weighments,
    long reservationId,
    string reason) : IApplicationCommand<TicketReservation>, IValidatable, IRequiresPermission
{
    private readonly IWeighmentService _weighments = weighments
        ?? throw new ArgumentNullException(nameof(weighments));

    /// <inheritdoc />
    public string Name => "Cancel ticket reservation";

    /// <inheritdoc />
    public Permission? RequiredPermission => Permissions.WeighmentCreate;

    /// <inheritdoc />
    public Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (reservationId <= 0)
        {
            return Task.FromResult(ValidationResult.Failure("ReservationId", "A valid reservation ID is required."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Task.FromResult(ValidationResult.Failure("Reason", "A cancellation reason is required."));
        }

        return Task.FromResult(ValidationResult.Success);
    }

    /// <inheritdoc />
    public async Task<CommandResult<TicketReservation>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ReportStatus("Cancelling ticket reservation…");

        var reservation = await _weighments
            .CancelReservationAsync(reservationId, reason, context.CancellationToken)
            .ConfigureAwait(false);

        context.Audit("SlipNumber", reservation.SlipNumber)
            .Audit("ReservationId", reservation.Id.ToString())
            .Audit("Reason", reason);

        return CommandResult<TicketReservation>.Success(
            reservation,
            $"Ticket {reservation.SlipNumber} cancelled.");
    }

    /// <inheritdoc />
    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

/// <summary>
/// Atomically creates a weighment and records the first weight using a pre-allocated ticket reservation.
/// </summary>
public sealed class RecordFirstWeightWithReservationCommand(
    IWeighmentService weighments,
    long reservationId,
    NewWeighment request,
    decimal kilograms,
    WeightSource source) : IApplicationCommand<Weighment>, IValidatable, IRequiresPermission
{
    private static readonly NewWeighmentValidator Rules = new();

    private readonly IWeighmentService _weighments = weighments
        ?? throw new ArgumentNullException(nameof(weighments));

    private readonly NewWeighment _request = request
        ?? throw new ArgumentNullException(nameof(request));

    /// <inheritdoc />
    public string Name => "Record first weight";

    /// <inheritdoc />
    public Permission? RequiredPermission => Permissions.WeighmentCreate;

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (reservationId <= 0)
        {
            return ValidationResult.Failure("ReservationId", "A valid ticket reservation is required.");
        }

        if (WeighmentRules.CheckWeight(kilograms) is { } weightProblem)
        {
            return weightProblem;
        }

        var requestValidation = await Rules.ValidateAsync(_request, cancellationToken).ConfigureAwait(false);
        if (!requestValidation.IsValid)
        {
            return requestValidation;
        }

        var reservation = await _weighments.GetReservationAsync(reservationId, cancellationToken).ConfigureAwait(false);
        if (reservation is null)
        {
            return ValidationResult.Failure("ReservationId", $"Ticket reservation {reservationId} was not found.");
        }

        if (reservation.Status != TicketReservationStatus.Reserved)
        {
            return ValidationResult.Failure(
                "Status",
                $"Ticket reservation {reservation.SlipNumber} is in '{reservation.Status}' status and cannot take a weight.");
        }

        return ValidationResult.Success;
    }

    /// <inheritdoc />
    public async Task<CommandResult<Weighment>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ReportStatus("Recording first weight…");

        var weighment = await _weighments
            .CreateWithReservationAndRecordFirstWeightAsync(reservationId, _request, kilograms, source, context.CancellationToken)
            .ConfigureAwait(false);

        context.Audit("SlipNumber", weighment.SlipNumber)
            .Audit("VehicleNumber", weighment.VehicleNumber)
            .Audit("Kilograms", kilograms)
            .Audit("WeightSource", source.ToString());

        return CommandResult<Weighment>.Success(
            weighment,
            $"{kilograms:0.##} kg recorded on {weighment.SlipNumber}. {weighment.NextAction} when the vehicle returns.");
    }

    /// <inheritdoc />
    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

/// <summary>
/// Atomically creates and completes a single-entry weighment using a pre-allocated ticket reservation.
/// </summary>
public sealed class RecordSingleEntryWeightWithReservationCommand(
    IWeighmentService weighments,
    long reservationId,
    NewWeighment request,
    decimal kilograms,
    WeightSource source,
    decimal tareWeightKg) : IApplicationCommand<Weighment>, IValidatable, IRequiresPermission
{
    private static readonly NewWeighmentValidator Rules = new();

    private readonly IWeighmentService _weighments = weighments
        ?? throw new ArgumentNullException(nameof(weighments));

    private readonly NewWeighment _request = request
        ?? throw new ArgumentNullException(nameof(request));

    /// <inheritdoc />
    public string Name => "Record single-entry weight";

    /// <inheritdoc />
    public Permission? RequiredPermission => Permissions.WeighmentCreate;

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (reservationId <= 0)
        {
            return ValidationResult.Failure("ReservationId", "A valid ticket reservation is required.");
        }

        if (WeighmentRules.CheckWeight(kilograms) is { } weightProblem)
        {
            return weightProblem;
        }

        if (WeighmentRules.CheckWeight(tareWeightKg) is { } tareProblem)
        {
            return tareProblem;
        }

        var requestValidation = await Rules.ValidateAsync(_request, cancellationToken).ConfigureAwait(false);
        if (!requestValidation.IsValid)
        {
            return requestValidation;
        }

        var reservation = await _weighments.GetReservationAsync(reservationId, cancellationToken).ConfigureAwait(false);
        if (reservation is null)
        {
            return ValidationResult.Failure("ReservationId", $"Ticket reservation {reservationId} was not found.");
        }

        if (reservation.Status != TicketReservationStatus.Reserved)
        {
            return ValidationResult.Failure(
                "Status",
                $"Ticket reservation {reservation.SlipNumber} is in '{reservation.Status}' status and cannot take a weight.");
        }

        return ValidationResult.Success;
    }

    /// <inheritdoc />
    public async Task<CommandResult<Weighment>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ReportStatus("Recording single-entry weight…");

        var weighment = await _weighments
            .CreateWithReservationAndRecordSingleEntryWeightAsync(reservationId, _request, kilograms, source, tareWeightKg, context.CancellationToken)
            .ConfigureAwait(false);

        context.Audit("SlipNumber", weighment.SlipNumber)
            .Audit("VehicleNumber", weighment.VehicleNumber)
            .Audit("Kilograms", kilograms)
            .Audit("TareWeightKg", tareWeightKg)
            .Audit("NetWeightKg", weighment.NetWeightKg?.ToString() ?? "0");

        return CommandResult<Weighment>.Success(
            weighment,
            $"Single-entry weighment {weighment.SlipNumber} completed. Net {weighment.NetWeightKg:0.##} kg.");
    }

    /// <inheritdoc />
    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

/// <summary>
/// Records the first weight of an open weighment.
/// </summary>
/// <remarks>
/// Validation reads the weighment before the command runs, so a weight taken against the
/// wrong stage is reported as something the operator can fix rather than as a fault. The
/// aggregate refuses the same thing by throwing — deliberately, because a caller that is not
/// this command must not be able to get past it either.
/// </remarks>
public sealed class RecordFirstWeightCommand(
    IWeighmentService weighments,
    long weighmentId,
    decimal kilograms,
    WeightSource source) : IApplicationCommand<Weighment>, IValidatable, IRequiresPermission
{
    private readonly IWeighmentService _weighments = weighments
        ?? throw new ArgumentNullException(nameof(weighments));

    /// <inheritdoc />
    public string Name => "Record first weight";

    /// <inheritdoc />
    public Permission? RequiredPermission => Permissions.WeighmentCreate;

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (WeighmentRules.CheckWeight(kilograms) is { } weightProblem)
        {
            return weightProblem;
        }

        var weighment = await _weighments.GetAsync(weighmentId, cancellationToken).ConfigureAwait(false);

        if (weighment is null)
        {
            return WeighmentRules.NotFound(weighmentId);
        }

        return weighment.Status == WeighmentStatus.Created
            ? ValidationResult.Success
            : ValidationResult.Failure(
                nameof(Weighment.Status),
                weighment.Status == WeighmentStatus.AwaitingSecondWeight
                    ? $"The first weight for {weighment.SlipNumber} has already been recorded. Record the second weight instead."
                    : $"Weighment {weighment.SlipNumber} is {weighment.Status} and cannot take a weight.");
    }

    /// <inheritdoc />
    public async Task<CommandResult<Weighment>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ReportStatus("Recording weight…");

        var weighment = await _weighments
            .RecordFirstWeightAsync(weighmentId, kilograms, source, context.CancellationToken)
            .ConfigureAwait(false);

        context.Audit("SlipNumber", weighment.SlipNumber)
            .Audit("Kilograms", kilograms)
            .Audit("WeightSource", source.ToString());

        return CommandResult<Weighment>.Success(
            weighment,
            $"{kilograms:0.##} kg recorded on {weighment.SlipNumber}. {weighment.NextAction} when the vehicle returns.");
    }

    /// <inheritdoc />
    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

/// <summary>
/// Records the second weight, which completes the weighment.
/// </summary>
/// <remarks>
/// There is no separate command to complete a weighment, because there is no separate act:
/// the second weight fixes the net weight and closes the record in one step. A command that
/// only completed would have to be paired with one that only weighed, and a weighment could
/// then sit holding both weights while nobody was expected to do anything about it.
/// </remarks>
/// <summary>
/// Records the second weight and updates second entry details, which completes the weighment.
/// </summary>
public sealed class RecordSecondWeightCommand : IApplicationCommand<Weighment>, IValidatable, IRequiresPermission
{
    private readonly IWeighmentService _weighments;
    private readonly RecordSecondWeightRequest _request;

    public RecordSecondWeightCommand(IWeighmentService weighments, RecordSecondWeightRequest request)
    {
        _weighments = weighments ?? throw new ArgumentNullException(nameof(weighments));
        _request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public RecordSecondWeightCommand(
        IWeighmentService weighments,
        long weighmentId,
        decimal kilograms,
        WeightSource source)
        : this(weighments, new RecordSecondWeightRequest(weighmentId, kilograms, source))
    {
    }

    /// <inheritdoc />
    public string Name => "Record second weight";

    /// <inheritdoc />
    public Permission? RequiredPermission => Permissions.WeighmentEdit;

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (WeighmentRules.CheckWeight(_request.Kilograms) is { } weightProblem)
        {
            return weightProblem;
        }

        if (_request.SecondCharges < 0)
        {
            return ValidationResult.Failure(nameof(_request.SecondCharges), "Second charges cannot be negative.");
        }

        if (_request.NumberOfBags < 0)
        {
            return ValidationResult.Failure(nameof(_request.NumberOfBags), "Number of bags cannot be negative.");
        }

        if (_request.BagWeightKg < 0)
        {
            return ValidationResult.Failure(nameof(_request.BagWeightKg), "Bag weight cannot be negative.");
        }

        var weighment = await _weighments.GetAsync(_request.WeighmentId, cancellationToken).ConfigureAwait(false);

        if (weighment is null)
        {
            return WeighmentRules.NotFound(_request.WeighmentId);
        }

        if (weighment.Status != WeighmentStatus.AwaitingSecondWeight)
        {
            return ValidationResult.Failure(
                nameof(Weighment.Status),
                weighment.Status == WeighmentStatus.Created
                    ? $"The first weight for {weighment.SlipNumber} has not been recorded yet."
                    : $"Weighment {weighment.SlipNumber} is {weighment.Status} and cannot take another weight.");
        }

        var first = weighment.FirstWeight!.Kilograms;
        var effectiveMode = _request.ModeOverride ?? weighment.Mode;
        var gross = effectiveMode == WeighmentMode.GrossFirst ? first : _request.Kilograms;
        var tare = effectiveMode == WeighmentMode.GrossFirst ? _request.Kilograms : first;

        if (gross < tare)
        {
            return ValidationResult.Failure(
                nameof(_request.Kilograms),
                $"The gross weight ({gross:0.##} kg) must be greater than the tare weight ({tare:0.##} kg). "
                + "Check whether the vehicle arrived loaded or empty.");
        }

        var net = gross - tare;
        if (_request.NumberOfBags.HasValue && _request.BagWeightKg.HasValue)
        {
            var totalBagWeight = _request.NumberOfBags.Value * _request.BagWeightKg.Value;
            if (totalBagWeight > net)
            {
                return ValidationResult.Failure(
                    nameof(_request.NumberOfBags),
                    $"Total bag deduction ({totalBagWeight:0.##} kg) exceeds the net weight ({net:0.##} kg).");
            }
        }

        return ValidationResult.Success;
    }

    /// <inheritdoc />
    public async Task<CommandResult<Weighment>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ReportStatus("Recording second weight…");

        var weighment = await _weighments
            .RecordSecondWeightAsync(_request, context.CancellationToken)
            .ConfigureAwait(false);

        context.Audit("SlipNumber", weighment.SlipNumber)
            .Audit("Kilograms", _request.Kilograms)
            .Audit("WeightSource", _request.Source.ToString())
            .Audit("NetKilograms", weighment.NetWeightKg)
            .Audit("SecondCharges", _request.SecondCharges);

        return CommandResult<Weighment>.Success(
            weighment,
            $"Weighment {weighment.SlipNumber} completed. Net {weighment.NetWeightKg:0.##} kg.");
    }

    /// <inheritdoc />
    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

/// <summary>
/// Records the only live weight for a single-entry transaction and completes it using the approved tare value.
/// </summary>
public sealed class RecordSingleEntryWeightCommand(
    IWeighmentService weighments,
    RecordSingleEntryWeightRequest request) : IApplicationCommand<Weighment>, IValidatable, IRequiresPermission
{
    private readonly IWeighmentService _weighments = weighments
        ?? throw new ArgumentNullException(nameof(weighments));
    private readonly RecordSingleEntryWeightRequest _request = request
        ?? throw new ArgumentNullException(nameof(request));

    public string Name => "Record single-entry weight";

    public Permission? RequiredPermission => Permissions.WeighmentCreate;

    public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (WeighmentRules.CheckWeight(_request.Kilograms) is { } weightProblem)
        {
            return weightProblem;
        }

        if (WeighmentRules.CheckWeight(_request.TareWeightKg) is { } tareProblem)
        {
            return ValidationResult.Failure(nameof(_request.TareWeightKg), tareProblem.Blocking.First().Message);
        }

        var weighment = await _weighments.GetAsync(_request.WeighmentId, cancellationToken).ConfigureAwait(false);
        if (weighment is null)
        {
            return WeighmentRules.NotFound(_request.WeighmentId);
        }

        return weighment.Status == WeighmentStatus.Created
            ? ValidationResult.Success
            : ValidationResult.Failure(
                nameof(Weighment.Status),
                $"Weighment {weighment.SlipNumber} is {weighment.Status} and cannot be completed as a single-entry weighment.");
    }

    public async Task<CommandResult<Weighment>> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ReportStatus("Recording single-entry weight...");

        var weighment = await _weighments
            .RecordSingleEntryWeightAsync(_request, context.CancellationToken)
            .ConfigureAwait(false);

        context.Audit("SlipNumber", weighment.SlipNumber)
            .Audit("Kilograms", _request.Kilograms)
            .Audit("TareWeightKg", _request.TareWeightKg)
            .Audit("WeightSource", _request.Source.ToString())
            .Audit("NetKilograms", weighment.NetWeightKg);

        return CommandResult<Weighment>.Success(
            weighment,
            $"Single-entry weighment {weighment.SlipNumber} completed. Net {weighment.NetWeightKg:0.##} kg.");
    }

    async Task<CommandResult> IApplicationCommand.ExecuteAsync(CommandContext context)
        => await ExecuteAsync(context).ConfigureAwait(false);
}

/// <summary>
/// Abandons an open weighment, keeping the row and the reason.
/// </summary>
/// <remarks>
/// Requires <see cref="Permissions.WeighmentCancel"/>, which
/// <see cref="Roles.Operator"/> does not hold: an operator may weigh all day and cannot make
/// a started weighment disappear.
/// </remarks>
public sealed class CancelWeighmentCommand(IWeighmentService weighments, long weighmentId, string reason)
    : IApplicationCommand, IValidatable, IRequiresPermission
{
    private readonly IWeighmentService _weighments = weighments
        ?? throw new ArgumentNullException(nameof(weighments));

    /// <inheritdoc />
    public string Name => "Cancel weighment";

    /// <inheritdoc />
    public Permission? RequiredPermission => Permissions.WeighmentCancel;

    /// <inheritdoc />
    public async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return ValidationResult.Failure(
                nameof(reason),
                "Give a reason for cancelling. A cancellation with no reason is what an audit asks about.");
        }

        if (reason.Trim().Length > Weighment.TextMaxLength)
        {
            return ValidationResult.Failure(
                nameof(reason),
                $"Keep the reason to {Weighment.TextMaxLength} characters or fewer.");
        }

        var weighment = await _weighments.GetAsync(weighmentId, cancellationToken).ConfigureAwait(false);

        if (weighment is null)
        {
            return WeighmentRules.NotFound(weighmentId);
        }

        return weighment.IsOpen
            ? ValidationResult.Success
            : ValidationResult.Failure(
                nameof(Weighment.Status),
                weighment.Status == WeighmentStatus.Completed
                    ? $"Weighment {weighment.SlipNumber} is complete. A completed weighment is corrected by a new weighment, not by cancelling the record its slip was printed from."
                    : $"Weighment {weighment.SlipNumber} is already cancelled.");
    }

    /// <inheritdoc />
    public async Task<CommandResult> ExecuteAsync(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.ReportStatus("Cancelling weighment…");

        var weighment = await _weighments
            .CancelAsync(weighmentId, reason, context.CancellationToken)
            .ConfigureAwait(false);

        context.Audit("SlipNumber", weighment.SlipNumber).Audit("Reason", reason);

        return CommandResult.Success($"Weighment {weighment.SlipNumber} cancelled.");
    }
}

/// <summary>
/// The checks the weighment commands share.
/// </summary>
/// <remarks>
/// Not a validator: these are two rules used by three commands whose subjects are a weight
/// and an identity rather than a form. A <see cref="Validator{T}"/> would need a type per
/// command to hold them.
/// </remarks>
internal static class WeighmentRules
{
    /// <summary>Returns a finding when a typed weight is not plausible, or <c>null</c>.</summary>
    internal static ValidationResult? CheckWeight(decimal kilograms)
    {
        if (kilograms <= 0m)
        {
            return ValidationResult.Failure(nameof(kilograms), "Enter a weight greater than zero.");
        }

        return kilograms > Weighment.MaximumWeightKg
            ? ValidationResult.Failure(
                nameof(kilograms),
                $"{kilograms:0.##} kg is not a plausible vehicle weight. The limit is {Weighment.MaximumWeightKg:0} kg — check the decimal point.")
            : null;
    }

    /// <summary>The finding for a weighment that is not there.</summary>
    internal static ValidationResult NotFound(long weighmentId)
        => ValidationResult.Failure(
            nameof(weighmentId),
            $"Weighment {weighmentId} was not found. It may have been retired by an administrator.");
}
