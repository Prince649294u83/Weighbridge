using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WeighBridge.Core.Mvvm;

/// <summary>
/// Minimal <see cref="INotifyPropertyChanged"/> implementation used as the base for
/// every ViewModel and observable model in the application.
/// </summary>
/// <remarks>
/// Hand-rolled rather than taken from a MVVM toolkit so the foundation stays inside
/// the approved technology stack (.NET 8 BCL only).
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Assigns <paramref name="value"/> to <paramref name="field"/> and raises
    /// <see cref="PropertyChanged"/> when the value actually changed.
    /// </summary>
    /// <returns><c>true</c> when the backing field was updated.</returns>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Overload that invokes <paramref name="onChanged"/> after a successful update —
    /// useful when a property change must also refresh commands or dependent state.
    /// </summary>
    protected bool SetProperty<T>(ref T field, T value, Action onChanged, [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
        {
            return false;
        }

        onChanged();
        return true;
    }

    /// <summary>Raises <see cref="PropertyChanged"/> for the given property.</summary>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Raises <see cref="PropertyChanged"/> for several properties at once.</summary>
    protected void OnPropertiesChanged(params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }
}
