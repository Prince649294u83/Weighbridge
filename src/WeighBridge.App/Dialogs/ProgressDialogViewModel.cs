using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WeighBridge.Core.Dialogs;
using WeighBridge.Core.Mvvm;

namespace WeighBridge.App.Dialogs;

/// <summary>
/// Presentation state for <see cref="ProgressDialog"/>, and the
/// <see cref="IProgressReporter"/> handed to the operation it is covering.
/// </summary>
/// <remarks>
/// The operation usually runs on a worker thread, so every report is marshalled onto
/// the UI dispatcher before it touches an observable property.
/// </remarks>
public sealed class ProgressDialogViewModel : ObservableObject, IProgressReporter, IDisposable
{
    private readonly CancellationTokenSource _cancellationSource = new();
    private readonly Dispatcher _dispatcher;

    private string _status = string.Empty;
    private double _percentage;
    private bool _isIndeterminate = true;
    private bool _isCancelling;

    public ProgressDialogViewModel(string title, string status, bool isCancellable, bool isIndeterminate)
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        Title = title;
        _status = status;
        _isIndeterminate = isIndeterminate;
        IsCancellable = isCancellable;

        CancelCommand = new RelayCommand(Cancel, () => IsCancellable && !IsCancelling);
    }

    /// <summary>Dialog heading.</summary>
    public string Title { get; }

    /// <summary>Whether a cancel button is offered.</summary>
    public bool IsCancellable { get; }

    /// <summary>Cancels the covered operation.</summary>
    public RelayCommand CancelCommand { get; }

    /// <inheritdoc />
    public CancellationToken CancellationToken => _cancellationSource.Token;

    /// <summary>Current status line.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Completion percentage from 0 to 100.</summary>
    public double Percentage
    {
        get => _percentage;
        private set => SetProperty(ref _percentage, value);
    }

    /// <summary>True while the operation cannot report a meaningful percentage.</summary>
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    /// <summary>True once cancellation was requested but the operation has not yet ended.</summary>
    public bool IsCancelling
    {
        get => _isCancelling;
        private set
        {
            if (SetProperty(ref _isCancelling, value))
            {
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <inheritdoc />
    public void ReportStatus(string status) => Invoke(() => Status = status);

    /// <inheritdoc />
    public void ReportProgress(double percentage) => Invoke(() =>
    {
        IsIndeterminate = false;
        Percentage = Math.Clamp(percentage, 0d, 100d);
    });

    /// <inheritdoc />
    public void Report(double percentage, string status) => Invoke(() =>
    {
        IsIndeterminate = false;
        Percentage = Math.Clamp(percentage, 0d, 100d);
        Status = status;
    });

    /// <inheritdoc />
    public void ReportIndeterminate(string status) => Invoke(() =>
    {
        IsIndeterminate = true;
        Status = status;
    });

    public void Dispose() => _cancellationSource.Dispose();

    private void Cancel()
    {
        IsCancelling = true;
        Status = "Cancelling…";

        // Cancel() can throw if the source was already disposed by a racing completion.
        try
        {
            _cancellationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Invoke(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Background, action);
    }
}
