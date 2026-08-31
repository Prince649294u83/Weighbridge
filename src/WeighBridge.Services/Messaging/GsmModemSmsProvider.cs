using System.IO.Ports;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Messaging;

namespace WeighBridge.Services.Messaging;

/// <summary>
/// SMS delivery provider transmitting via a dedicated hardware GSM Modem using Hayes AT commands.
/// Strictly uses configured serial parameters with exclusive port locking; never performs port scanning or baud probing.
/// </summary>
public sealed class GsmModemSmsProvider : ISmsProvider, IDisposable
{
    public const string ProviderName = "GsmModem";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    private readonly IOptions<SmsOptions> _options;
    private readonly ILogger<GsmModemSmsProvider> _logger;
    private readonly SemaphoreSlim _portGate = new(1, 1);

    public GsmModemSmsProvider(
        IOptions<SmsOptions> options,
        ILogger<GsmModemSmsProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public async Task<SmsSendResult> SendAsync(
        string recipientPhoneNumber,
        string messageContent,
        CancellationToken cancellationToken = default)
    {
        var opt = _options.Value;

        if (string.IsNullOrWhiteSpace(opt.GsmPortName))
        {
            return SmsSendResult.ConfigurationFailure("GSM Modem COM port is not configured.");
        }

        if (string.IsNullOrWhiteSpace(recipientPhoneNumber))
        {
            return SmsSendResult.PermanentFailure("Recipient phone number is missing or empty.");
        }

        await _portGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        SerialPort? port = null;

        try
        {
            port = new SerialPort(opt.GsmPortName, opt.GsmBaudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = (int)CommandTimeout.TotalMilliseconds,
                WriteTimeout = (int)CommandTimeout.TotalMilliseconds,
                NewLine = "\r\n",
                Encoding = Encoding.ASCII
            };

            port.Open();

            // 1. Check modem responsiveness (AT -> OK)
            var response = await ExecuteCommandAsync(port, "AT", CommandTimeout, cancellationToken).ConfigureAwait(false);
            if (!response.Contains("OK", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("GSM Modem at {Port} did not return OK to AT check: {Response}", opt.GsmPortName, response);
                return SmsSendResult.TransientFailure($"Modem not ready: {response}");
            }

            // 2. Set SMS Text Mode (AT+CMGF=1 -> OK)
            response = await ExecuteCommandAsync(port, "AT+CMGF=1", CommandTimeout, cancellationToken).ConfigureAwait(false);
            if (!response.Contains("OK", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("GSM Modem failed to set text mode CMGF=1: {Response}", response);
                return SmsSendResult.TransientFailure($"Failed to set SMS text mode: {response}");
            }

            // 3. Initiate SMS send (AT+CMGS="<number>" -> awaits '> ')
            port.DiscardInBuffer();
            port.Write($"AT+CMGS=\"{recipientPhoneNumber}\"\r");

            var promptReceived = await AwaitPromptAsync(port, CommandTimeout, cancellationToken).ConfigureAwait(false);
            if (!promptReceived)
            {
                _logger.LogWarning("GSM Modem did not return prompt for CMGS to {Recipient}", recipientPhoneNumber);
                // Send ESC (0x1B) to abort
                port.Write(new byte[] { 0x1B }, 0, 1);
                return SmsSendResult.TransientFailure("Modem did not return input prompt ('>').");
            }

            // 4. Send message body followed by Ctrl+Z (0x1A)
            port.Write($"{messageContent}\x1A");

            // 5. Await delivery confirmation (+CMGS: <mr> ... OK)
            var confirmation = await ReadUntilTerminationAsync(port, TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);

            if (confirmation.Contains("OK", StringComparison.OrdinalIgnoreCase) || confirmation.Contains("+CMGS:", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("SMS successfully sent to {Recipient} via GSM Modem at {Port}", recipientPhoneNumber, opt.GsmPortName);
                return SmsSendResult.Success(confirmation.Trim());
            }

            _logger.LogWarning("GSM Modem send failed to {Recipient}: {Confirmation}", recipientPhoneNumber, confirmation);
            return SmsSendResult.TransientFailure($"Modem error: {confirmation}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "GSM Modem port {Port} access denied or in use", opt.GsmPortName);
            return SmsSendResult.TransientFailure($"Port {opt.GsmPortName} access denied: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "GSM Modem communication timed out on {Port}", opt.GsmPortName);
            return SmsSendResult.TransientFailure("Modem communication timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error communicating with GSM Modem on {Port}", opt.GsmPortName);
            return SmsSendResult.TransientFailure($"GSM communication error: {ex.Message}");
        }
        finally
        {
            if (port is { IsOpen: true })
            {
                try { port.Close(); } catch { }
            }
            port?.Dispose();
            _portGate.Release();
        }
    }

    private static async Task<string> ExecuteCommandAsync(
        SerialPort port,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        port.DiscardInBuffer();
        port.WriteLine(command);
        return await ReadUntilTerminationAsync(port, timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> AwaitPromptAsync(
        SerialPort port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (port.BytesToRead > 0)
            {
                int b = port.ReadByte();
                if (b == '>') return true;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    private static async Task<string> ReadUntilTerminationAsync(
        SerialPort port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (port.BytesToRead > 0)
            {
                string chunk = port.ReadExisting();
                sb.Append(chunk);

                var current = sb.ToString();
                if (current.Contains("OK", StringComparison.OrdinalIgnoreCase) ||
                    current.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    return current;
                }
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return sb.ToString();
    }

    public void Dispose()
    {
        _portGate.Dispose();
    }
}
