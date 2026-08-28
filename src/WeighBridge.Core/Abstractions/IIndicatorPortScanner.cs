namespace WeighBridge.Core.Abstractions;

/// <summary>
/// The outcome of probing one serial port at one baud rate.
/// </summary>
/// <param name="PortName">The port that was probed, e.g. <c>COM3</c>.</param>
/// <param name="BaudRate">The baud rate the probe used.</param>
/// <param name="SpeaksProtocol">
/// True only when at least one frame read from the port decoded to a weight through the
/// configured protocol parser.
/// </param>
/// <param name="SampleWeightKg">The weight the decoded frame carried, when one decoded.</param>
/// <param name="RawSample">
/// The raw frame text, for the operator to compare against the indicator's display.
/// </param>
/// <param name="Detail">A sentence describing what happened, suitable for the screen.</param>
public sealed record PortProbeResult(
    string PortName,
    int BaudRate,
    bool SpeaksProtocol,
    decimal? SampleWeightKg,
    string? RawSample,
    string Detail);

/// <summary>
/// Finds which serial port a weight indicator is plugged into.
/// </summary>
/// <remarks>
/// <para>
/// Detection means decoding, not enumerating. Listing the ports Windows reports is the
/// easy half and answers nothing useful: a terminal typically has several, and on a
/// weighbridge the consequence of picking the wrong one is a slip with a weight that came
/// from some other device. So a port is only reported as found when bytes read from it
/// pass through the same frame extractor and protocol parser the live driver uses and
/// yield an actual weight — the raw frame comes back with it, so the operator can check
/// the number against the indicator's own display before committing to the port.
/// </para>
/// <para>
/// A probe opens the port, which means the running driver must not be holding it. Callers
/// disconnect the indicator first and reconnect afterwards.
/// </para>
/// </remarks>
public interface IIndicatorPortScanner
{
    /// <summary>
    /// The serial ports Windows currently reports, sorted by number.
    /// </summary>
    /// <remarks>
    /// Read live on every call: a USB-to-serial adapter appears and disappears with the
    /// cable, so a cached list goes stale the moment someone unplugs one.
    /// </remarks>
    IReadOnlyList<string> GetAvailablePorts();

    /// <summary>
    /// Probes every available port until one decodes a weight, and returns what each
    /// attempt found.
    /// </summary>
    /// <param name="portNames">
    /// Ports to probe, or <c>null</c> for every port <see cref="GetAvailablePorts"/> reports.
    /// </param>
    /// <param name="baudRates">
    /// Baud rates to try per port, in order, or <c>null</c> for the configured rate followed
    /// by the standard industrial rates.
    /// </param>
    /// <param name="listenPerAttempt">
    /// How long to listen on each port/baud combination. An indicator in continuous mode
    /// sends several frames a second; one that only answers a poll request will not be found
    /// by listening and has to be configured by hand.
    /// </param>
    Task<IReadOnlyList<PortProbeResult>> ScanAsync(
        IEnumerable<string>? portNames = null,
        IEnumerable<int>? baudRates = null,
        TimeSpan? listenPerAttempt = null,
        CancellationToken cancellationToken = default);
}
