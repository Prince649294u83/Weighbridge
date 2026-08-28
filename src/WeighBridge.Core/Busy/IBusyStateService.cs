using System.ComponentModel;

namespace WeighBridge.Core.Busy;

/// <summary>
/// Tracks whether the application is working on something, so the shell can say so once
/// rather than every module inventing its own spinner.
/// </summary>
/// <remarks>
/// <para>
/// Nested by design. Saving a weighment prints a slip, and printing a slip renders a
/// template: three overlapping operations, one busy indicator. The application stays busy
/// until the outermost one ends.
/// </para>
/// <para>
/// A scope is released when it is disposed, on every path out of the operation — success,
/// failure, cancellation, exception. That is the whole reason the service hands back a
/// disposable instead of exposing a settable flag: a flag can be left set, and an
/// application stuck in "busy" needs restarting.
/// </para>
/// <para>
/// Safe from any thread; the implementation marshals onto the user-interface thread before
/// touching bound state.
/// </para>
/// </remarks>
public interface IBusyStateService : INotifyPropertyChanged
{
    /// <summary>True while at least one operation is in flight.</summary>
    bool IsBusy { get; }

    /// <summary>How many operations are in flight.</summary>
    int ActiveCount { get; }

    /// <summary>
    /// The operation the indicator should describe — the most recently started one, since
    /// that is what the operator just did.
    /// </summary>
    IBusyOperation? Current { get; }

    /// <summary>Every operation in flight, outermost first.</summary>
    IReadOnlyList<IBusyOperation> Active { get; }

    /// <summary>
    /// Raised whenever the set of operations in flight changes — including a nested begin or
    /// release that leaves <see cref="IsBusy"/> alone, since
    /// <see cref="BusyStateChangedEventArgs.Current"/> and
    /// <see cref="BusyStateChangedEventArgs.ActiveCount"/> changed even when the flag did not.
    /// </summary>
    event EventHandler<BusyStateChangedEventArgs>? BusyStateChanged;

    /// <summary>
    /// Begins an operation.
    /// </summary>
    /// <param name="title">What the operator should be told is happening.</param>
    /// <param name="isCancellable">Whether to offer a cancel affordance.</param>
    /// <param name="cancellationToken">
    /// Caller's token. The scope's own token is linked to it, so a shutdown cancels the
    /// operation as well as the operator's cancel button.
    /// </param>
    /// <returns>
    /// A scope that must be disposed. Asynchronous because the state it raises is bound to
    /// the user interface and may need a thread hop first.
    /// </returns>
    Task<IBusyScope> BeginAsync(
        string title,
        bool isCancellable = false,
        CancellationToken cancellationToken = default);
}

/// <summary>What the shell needs to know about one operation in flight.</summary>
public interface IBusyOperation : INotifyPropertyChanged
{
    /// <summary>Identity of this operation, for correlating with a log entry.</summary>
    Guid Id { get; }

    /// <summary>What the operator is told is happening.</summary>
    string Title { get; }

    /// <summary>Detail line beneath the title, when the operation reports one.</summary>
    string? Status { get; }

    /// <summary>Completion from 0 to 100, meaningful only when not indeterminate.</summary>
    double Percentage { get; }

    /// <summary>True while the operation cannot say how far along it is.</summary>
    bool IsIndeterminate { get; }

    /// <summary>Whether a cancel affordance should be offered.</summary>
    bool IsCancellable { get; }

    /// <summary>True once cancellation was asked for but the operation has not yet ended.</summary>
    bool IsCancelling { get; }

    /// <summary>When the operation started.</summary>
    DateTimeOffset StartedAt { get; }
}

/// <summary>
/// One operation's hold on the busy indicator, and its handle for reporting progress.
/// </summary>
/// <remarks>
/// Implements <see cref="IProgressSink"/> so the operation can report through the same
/// object it holds, and <see cref="IDisposable"/> rather than <see cref="IAsyncDisposable"/>
/// so the intended shape compiles as one line:
/// <c>using var busy = await busyService.BeginAsync("Saving…");</c>
/// </remarks>
public interface IBusyScope : IProgressSink, IDisposable
{
    /// <summary>The operation this scope holds.</summary>
    IBusyOperation Operation { get; }

    /// <summary>
    /// Cancelled when the operator cancels or the caller's own token is cancelled.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>True once this scope has been released.</summary>
    bool IsCompleted { get; }

    /// <summary>Asks the operation to stop. Does nothing when it is not cancellable.</summary>
    void Cancel();
}

/// <summary>
/// Where an operation reports what it is doing.
/// </summary>
/// <remarks>
/// Deliberately the same shape as <see cref="Dialogs.IProgressReporter"/>, which the
/// progress dialog already implements, so a long operation can be written once and shown
/// either in a modal dialog or in the shell's status area.
/// </remarks>
public interface IProgressSink
{
    /// <summary>Updates the detail line.</summary>
    void ReportStatus(string status);

    /// <summary>Updates the percentage. Values outside 0-100 are clamped.</summary>
    void ReportProgress(double percentage);

    /// <summary>Updates percentage and detail together.</summary>
    void Report(double percentage, string status);

    /// <summary>Switches to an indeterminate indicator.</summary>
    void ReportIndeterminate(string status);
}

/// <summary>Carries the busy state as it changed.</summary>
public sealed class BusyStateChangedEventArgs(bool isBusy, int activeCount, IBusyOperation? current) : EventArgs
{
    /// <summary>Whether the application is busy now.</summary>
    public bool IsBusy { get; } = isBusy;

    /// <summary>How many operations are in flight now.</summary>
    public int ActiveCount { get; } = activeCount;

    /// <summary>The operation the indicator now describes, if any.</summary>
    public IBusyOperation? Current { get; } = current;
}
