using WeighBridge.Core.Mvvm;

namespace WeighBridge.Tests.Mvvm;

/// <summary>
/// Covers the MVVM command primitives every module will bind to.
/// </summary>
public sealed class CommandTests
{
    [Fact]
    public void RelayCommand_Execute_InvokesTheAction()
    {
        var invoked = 0;
        var command = new RelayCommand(() => invoked++);

        command.Execute(null);

        Assert.Equal(1, invoked);
    }

    [Fact]
    public void RelayCommand_WithFalseCanExecute_DoesNotInvoke()
    {
        var invoked = 0;
        var command = new RelayCommand(() => invoked++, () => false);

        Assert.False(command.CanExecute(null));
        command.Execute(null);

        Assert.Equal(0, invoked);
    }

    [Fact]
    public void RelayCommand_NotifyCanExecuteChanged_RaisesTheEvent()
    {
        var command = new RelayCommand(() => { });
        var raised = 0;
        command.CanExecuteChanged += (_, _) => raised++;

        command.NotifyCanExecuteChanged();

        Assert.Equal(1, raised);
    }

    [Fact]
    public void RelayCommandOfT_PassesTheTypedParameter()
    {
        string? received = null;
        var command = new RelayCommand<string>(value => received = value);

        command.Execute("Vehicle Entry");

        Assert.Equal("Vehicle Entry", received);
    }

    [Fact]
    public void RelayCommandOfT_WithMismatchedParameter_DoesNotThrow()
    {
        // WPF can hand a command any object; a bad binding must not crash the app.
        var command = new RelayCommand<string>(_ => { });

        command.Execute(42);
    }

    [Fact]
    public async Task AsyncRelayCommand_ExecuteAsync_AwaitsTheOperation()
    {
        var completed = false;
        var command = new AsyncRelayCommand(async () =>
        {
            await Task.Delay(10);
            completed = true;
        });

        await command.ExecuteAsync();

        Assert.True(completed);
        Assert.False(command.IsRunning);
    }

    [Fact]
    public async Task AsyncRelayCommand_WhileRunning_CannotExecuteAgain()
    {
        // Guards against an operator double-clicking a save button.
        var gate = new TaskCompletionSource();
        var starts = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            starts++;
            await gate.Task;
        });

        var first = command.ExecuteAsync();

        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));

        command.Execute(null);
        Assert.Equal(1, starts);

        gate.SetResult();
        await first;

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task AsyncRelayCommand_WhenOperationThrows_RoutesToOnErrorAndResets()
    {
        // A failing command must not leave the UI permanently disabled, and the
        // exception must reach the handler rather than becoming an unobserved task.
        Exception? captured = null;
        var command = new AsyncRelayCommand(
            () => throw new InvalidOperationException("boom"),
            onError: ex => captured = ex);

        await command.ExecuteAsync();

        Assert.IsType<InvalidOperationException>(captured);
        Assert.False(command.IsRunning);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task AsyncRelayCommandOfT_PassesTheTypedParameter()
    {
        int? received = null;
        var command = new AsyncRelayCommand<int>(value =>
        {
            received = value;
            return Task.CompletedTask;
        });

        await command.ExecuteAsync(7);

        Assert.Equal(7, received);
    }
}
