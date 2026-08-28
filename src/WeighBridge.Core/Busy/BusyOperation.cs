using WeighBridge.Core.Mvvm;

namespace WeighBridge.Core.Busy;

/// <summary>
/// One operation in flight, observable so the shell binds straight to it.
/// </summary>
/// <remarks>
/// Read-only from outside for the same reason as
/// <see cref="Tasks.TaskRegistration"/> and <see cref="Health.HealthEntry"/>: the scope owns
/// the transitions. The setters are public only to the service that created it — enforced by
/// convention here rather than by <c>internal</c>, because the service lives in another
/// assembly.
/// </remarks>
public sealed class BusyOperation : ObservableObject, IBusyOperation
{
    private string? _status;
    private double _percentage;
    private bool _isIndeterminate = true;
    private bool _isCancelling;

    /// <summary>Creates an operation that has just started.</summary>
    public BusyOperation(string title, bool isCancellable, DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Id = Guid.NewGuid();
        Title = title;
        IsCancellable = isCancellable;
        StartedAt = startedAt;
    }

    /// <inheritdoc />
    public Guid Id { get; }

    /// <inheritdoc />
    public string Title { get; }

    /// <inheritdoc />
    public bool IsCancellable { get; }

    /// <inheritdoc />
    public DateTimeOffset StartedAt { get; }

    /// <inheritdoc />
    public string? Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <inheritdoc />
    public double Percentage
    {
        get => _percentage;
        set => SetProperty(ref _percentage, Math.Clamp(value, 0d, 100d));
    }

    /// <inheritdoc />
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    /// <inheritdoc />
    public bool IsCancelling
    {
        get => _isCancelling;
        set => SetProperty(ref _isCancelling, value);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Title} ({(IsIndeterminate ? "working" : $"{Percentage:F0}%")})";
}
