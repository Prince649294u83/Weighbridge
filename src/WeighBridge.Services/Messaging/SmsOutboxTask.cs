using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeighBridge.Core.Configuration;
using WeighBridge.Core.Messaging;
using WeighBridge.Core.Tasks;

namespace WeighBridge.Services.Messaging;

/// <summary>
/// Recurring background task that periodically flushes and transmits pending and retryable
/// SMS messages from the durable SQLite outbox table.
/// </summary>
public sealed class SmsOutboxTask : RecurringTask
{
    private readonly ISmsService _smsService;
    private readonly IOptionsMonitor<SmsOptions> _smsOptions;
    private readonly ILogger<SmsOutboxTask> _logger;

    public SmsOutboxTask(
        ISmsService smsService,
        IOptionsMonitor<SmsOptions> smsOptions,
        ILogger<SmsOutboxTask> logger)
        : base(
            "sms-outbox-dispatcher",
            "Dispatches pending SMS notifications from outbox queue",
            TimeSpan.FromSeconds(10),
            BackgroundTaskPriority.Normal)
    {
        _smsService = smsService ?? throw new ArgumentNullException(nameof(smsService));
        _smsOptions = smsOptions ?? throw new ArgumentNullException(nameof(smsOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public override async Task ExecuteAsync(
        IProgress<BackgroundTaskProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (!_smsOptions.CurrentValue.Enabled)
        {
            progress.Report(new BackgroundTaskProgress("SMS subsystem disabled in configuration", 100));
            return;
        }

        progress.Report(new BackgroundTaskProgress("Checking SMS outbox queue"));

        try
        {
            var processed = await _smsService.ProcessOutboxAsync(cancellationToken).ConfigureAwait(false);
            if (processed > 0)
            {
                progress.Report(new BackgroundTaskProgress($"Dispatched {processed} SMS notification(s)", 100));
            }
            else
            {
                progress.Report(new BackgroundTaskProgress("Outbox empty or up to date", 100));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background error while flushing SMS outbox");
            progress.Report(new BackgroundTaskProgress($"Error flushing outbox: {ex.Message}", 100));
        }
    }
}
