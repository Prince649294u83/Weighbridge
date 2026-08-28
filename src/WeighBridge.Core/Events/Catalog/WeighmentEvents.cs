using WeighBridge.Domain.Enums;

namespace WeighBridge.Core.Events.Catalog;

/// <summary>
/// Announces that a weighment was opened.
/// </summary>
/// <remarks>
/// Published once the row is committed, never before — a subscriber that reacted to an
/// intention rather than a fact would print a slip for a weighment that never saved.
/// </remarks>
public sealed class WeighmentCreatedEvent(
    long weighmentId,
    string slipNumber,
    string vehicleNumber,
    WeighmentMode mode,
    string? source = null) : ApplicationEvent(source)
{
    /// <summary>Identity of the weighment.</summary>
    public long WeighmentId { get; } = weighmentId;

    /// <summary>Human-readable slip number.</summary>
    public string SlipNumber { get; } = slipNumber;

    /// <summary>Registration number of the vehicle.</summary>
    public string VehicleNumber { get; } = vehicleNumber;

    /// <summary>Whether the first weight is the gross or the tare.</summary>
    public WeighmentMode Mode { get; } = mode;
}

/// <summary>
/// Announces the first weight of a weighment.
/// </summary>
/// <remarks>
/// The vehicle is now on site with a weight against it. This is what a yard display, an
/// occupancy count or a camera capture subscribes to.
/// </remarks>
public sealed class FirstWeightRecordedEvent(
    long weighmentId,
    string slipNumber,
    string vehicleNumber,
    decimal kilograms,
    WeightSource weightSource,
    string? source = null) : ApplicationEvent(source)
{
    /// <summary>Identity of the weighment.</summary>
    public long WeighmentId { get; } = weighmentId;

    /// <summary>Human-readable slip number.</summary>
    public string SlipNumber { get; } = slipNumber;

    /// <summary>Registration number of the vehicle.</summary>
    public string VehicleNumber { get; } = vehicleNumber;

    /// <summary>The weight recorded, in kilograms.</summary>
    public decimal Kilograms { get; } = kilograms;

    /// <summary>Whether the indicator supplied the reading or an operator typed it.</summary>
    public WeightSource WeightSource { get; } = weightSource;
}

/// <summary>
/// Announces the second weight of a weighment.
/// </summary>
/// <remarks>
/// Always accompanied by a <see cref="WeighmentCompletedEvent"/>, because the second weight
/// completes the weighment. Both exist because they are different facts to different
/// subscribers: this one is about a weighing, the other about a finished transaction.
/// </remarks>
public sealed class SecondWeightRecordedEvent(
    long weighmentId,
    string slipNumber,
    string vehicleNumber,
    decimal kilograms,
    WeightSource weightSource,
    string? source = null) : ApplicationEvent(source)
{
    /// <summary>Identity of the weighment.</summary>
    public long WeighmentId { get; } = weighmentId;

    /// <summary>Human-readable slip number.</summary>
    public string SlipNumber { get; } = slipNumber;

    /// <summary>Registration number of the vehicle.</summary>
    public string VehicleNumber { get; } = vehicleNumber;

    /// <summary>The weight recorded, in kilograms.</summary>
    public decimal Kilograms { get; } = kilograms;

    /// <summary>Whether the indicator supplied the reading or an operator typed it.</summary>
    public WeightSource WeightSource { get; } = weightSource;
}

/// <summary>
/// Announces a completed weighment, with the figures the slip will carry.
/// </summary>
/// <remarks>
/// The event the printing, reporting and dashboard modules care about. Carries the numbers
/// rather than only the identity so a subscriber does not have to go back to the database
/// for what the publisher already had in hand.
/// </remarks>
public sealed class WeighmentCompletedEvent(
    long weighmentId,
    string slipNumber,
    string vehicleNumber,
    decimal grossKilograms,
    decimal tareKilograms,
    decimal netKilograms,
    string? source = null) : ApplicationEvent(source)
{
    /// <summary>Identity of the weighment.</summary>
    public long WeighmentId { get; } = weighmentId;

    /// <summary>Human-readable slip number.</summary>
    public string SlipNumber { get; } = slipNumber;

    /// <summary>Registration number of the vehicle.</summary>
    public string VehicleNumber { get; } = vehicleNumber;

    /// <summary>Gross weight in kilograms.</summary>
    public decimal GrossKilograms { get; } = grossKilograms;

    /// <summary>Tare weight in kilograms.</summary>
    public decimal TareKilograms { get; } = tareKilograms;

    /// <summary>Net weight in kilograms — always gross minus tare.</summary>
    public decimal NetKilograms { get; } = netKilograms;
}

/// <summary>
/// Announces an abandoned weighment.
/// </summary>
/// <remarks>
/// Carries the reason because that is the part an auditor asks about. A cancellation with no
/// recorded reason is the pattern a fraud investigation looks for.
/// </remarks>
public sealed class WeighmentCancelledEvent(
    long weighmentId,
    string slipNumber,
    string vehicleNumber,
    string reason,
    string? source = null) : ApplicationEvent(source)
{
    /// <summary>Identity of the weighment.</summary>
    public long WeighmentId { get; } = weighmentId;

    /// <summary>Human-readable slip number.</summary>
    public string SlipNumber { get; } = slipNumber;

    /// <summary>Registration number of the vehicle.</summary>
    public string VehicleNumber { get; } = vehicleNumber;

    /// <summary>Why the weighment was abandoned.</summary>
    public string Reason { get; } = reason;
}
