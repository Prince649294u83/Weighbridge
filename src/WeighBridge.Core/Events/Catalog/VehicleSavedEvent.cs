namespace WeighBridge.Core.Events.Catalog;

/// <summary>
/// Announces that a weighment record was persisted.
/// </summary>
/// <remarks>
/// Nothing publishes this yet — the Vehicle Entry module will. It exists now so the
/// dashboard, reports, audit log, camera and printer subscribers can be written against
/// a stable contract, and so the shape of an event is established before a dozen of them
/// are written by different hands.
/// </remarks>
public sealed class VehicleSavedEvent : ApplicationEvent
{
    /// <summary>Creates the event.</summary>
    /// <param name="ticketNumber">Identifier of the saved weighment slip.</param>
    /// <param name="vehicleNumber">Registration number of the vehicle.</param>
    /// <param name="isComplete">
    /// True when the second weight completed the weighment, false when only the first
    /// weight has been taken and the record is pending.
    /// </param>
    /// <param name="source">Component that raised the event.</param>
    public VehicleSavedEvent(string ticketNumber, string vehicleNumber, bool isComplete, string? source = null)
        : base(source)
    {
        TicketNumber = ticketNumber;
        VehicleNumber = vehicleNumber;
        IsComplete = isComplete;
    }

    /// <summary>Identifier of the saved weighment slip.</summary>
    public string TicketNumber { get; }

    /// <summary>Registration number of the vehicle.</summary>
    public string VehicleNumber { get; }

    /// <summary>True when the weighment is finished rather than pending a second weight.</summary>
    public bool IsComplete { get; }
}
