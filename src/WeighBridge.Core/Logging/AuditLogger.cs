using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WeighBridge.Core.Application;
using WeighBridge.Core.Diagnostics;
using WeighBridge.Core.Security;

namespace WeighBridge.Core.Logging;

/// <summary>
/// Writes the audit trail: to the file log for immediate reading, and â€” when a durable
/// store is registered â€” to the database, where it survives the terminal.
/// </summary>
/// <remarks>
/// <para>
/// Entries are written at information level and above, and the audit category should never
/// be filtered below that in configuration â€” an audit trail with gaps is worse than none,
/// because it looks complete.
/// </para>
/// <para>
/// Database writes run on a dedicated background thread so an operator's command is never
/// waiting on storage. The queue is bounded; if it ever saturates, records are dropped
/// rather than allowed to stall the application, and every drop is counted and reported â€”
/// a silently thinning trail would look exactly like a complete one.
/// </para>
/// </remarks>
public sealed class AuditLogger : CategoryLoggerBase, IAuditLogger, IDisposable
{
    private const int QueueCapacity = 10_000;

    private readonly IAuditStore? _auditStore;
    private readonly BlockingCollection<AuditRecord>? _queue;
    private readonly Thread? _writer;
    private readonly SignedInOperator? _operator;

    private int _dropped;
    private int _disposed;

    public AuditLogger(
        ILoggerFactory loggerFactory,
        IApplicationInfoService applicationInfo,
        IAuditStore? auditStore = null,
        SignedInOperator? signedInOperator = null)
        : base(LogCategory.Audit, loggerFactory, applicationInfo, signedInOperator)
    {
        _auditStore = auditStore;
        _operator = signedInOperator;

        if (auditStore is not null)
        {
            _queue = new BlockingCollection<AuditRecord>(QueueCapacity);
            _writer = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "WeighBridge.AuditWriter",
            };
            _writer.Start();
        }
    }

    /// <inheritdoc />
    public void Record(string action, string entity, string? entityId = null, string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);

        Enqueue("Succeeded", action, entity, entityId, details);

        Write(
            LogLevel.Information,
            null,
            "{Action} {Entity} {EntityId} {Details}",
            [action, entity, entityId ?? "-", details ?? string.Empty]);
    }

    /// <inheritdoc />
    public void RecordDenied(string action, string entity, string reason, string? entityId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Enqueue("Denied", action, entity, entityId, reason);

        Write(
            LogLevel.Warning,
            null,
            "DENIED {Action} {Entity} {EntityId}: {Reason}",
            [action, entity, entityId ?? "-", reason]);
    }

    /// <inheritdoc />
    public void RecordFailed(string action, string entity, string reason, string? entityId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Enqueue("Failed", action, entity, entityId, reason);

        Write(
            LogLevel.Warning,
            null,
            "FAILED {Action} {Entity} {EntityId}: {Reason}",
            [action, entity, entityId ?? "-", reason]);
    }

    /// <summary>
    /// Stops accepting records, drains what was accepted, and reports anything dropped.
    /// Idempotent: shutdown disposes this explicitly so the final records reach the
    /// database before their dependencies close, and the container disposes it again.
    /// </summary>
    public void Dispose()
    {
        if (_queue is null || Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.CompleteAdding();

        // Bounded join: the writer drains quickly, and a wedged one must not hang shutdown.
        if (_writer is { } writer && writer.IsAlive && !writer.Join(TimeSpan.FromSeconds(3)))
        {
            Write(LogLevel.Error, null, "Audit writer did not stop within 3s; remaining records were drained synchronously.", []);
        }

        DrainRemainingSynchronously();
        _queue.Dispose();

        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
        {
            Write(
                LogLevel.Error,
                null,
                "{Count} audit record(s) were dropped because the queue saturated. The file log above still carries them.",
                [dropped]);
        }
    }

    private void Enqueue(string outcome, string action, string entity, string? entityId, string? details)
    {
        if (_queue is null)
        {
            // No durable store registered (tests, headless hosts). The file log above is
            // the entire trail, which is the historical behaviour.
            return;
        }

        var record = new AuditRecord(
            DateTime.UtcNow,
            _operator?.UserName,
            ModuleScope.Current,
            action,
            outcome,
            entity,
            entityId,
            details,
            CorrelationScope.CurrentOrNew);

        try
        {
            if (!_queue.TryAdd(record))
            {
                // Bounded queue saturated. Count it: a silently thinning audit trail would
                // look exactly like a complete one.
                Interlocked.Increment(ref _dropped);
            }
        }
        catch (InvalidOperationException)
        {
            // Raced CompleteAdding during shutdown. The entry lives on in the file log.
        }
    }

    private void WriteLoop()
    {
        // GetConsumingEnumerable ends when CompleteAdding is called and the queue empties,
        // which is exactly the Dispose contract.
        foreach (var record in _queue!.GetConsumingEnumerable())
        {
            try
            {
                _auditStore!.WriteAsync(record).AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                // The store promised not to throw; belt and braces anyway. Losing one row
                // to a broken store must not take down the writer for all of them.
                Write(
                    LogLevel.Warning,
                    exception,
                    "Audit store rejected a record for action {Action}; continuing.",
                    [record.Action]);
            }
        }
    }

    private void DrainRemainingSynchronously()
    {
        var queue = _queue;
        if (queue is null)
        {
            return;
        }

        while (queue.TryTake(out var record))
        {
            try
            {
                _auditStore!.WriteAsync(record).AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // Shutdown-time best effort. The record is in the file log regardless.
            }
        }
    }
}
