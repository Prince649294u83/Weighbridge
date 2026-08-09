using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WeighBridge.Core.Mvvm;
using WeighBridge.Core.Navigation;
using WeighBridge.Services.Navigation;

namespace WeighBridge.Tests.Navigation;

/// <summary>
/// Covers the back/forward history contract of the single-window shell.
/// </summary>
public sealed class NavigationServiceTests
{
    private sealed class FirstViewModel : ViewModelBase;

    private sealed class SecondViewModel : ViewModelBase;

    private sealed class ThirdViewModel : ViewModelBase;

    private static NavigationService CreateService()
    {
        // A real container, so the test also proves ViewModels are resolved by injection
        // rather than constructed ad hoc.
        var services = new ServiceCollection()
            .AddTransient<FirstViewModel>()
            .AddTransient<SecondViewModel>()
            .AddTransient<ThirdViewModel>()
            .BuildServiceProvider();

        return new NavigationService(services, NullLogger<NavigationService>.Instance);
    }

    [Fact]
    public async Task NavigateToAsync_SetsCurrentViewModel()
    {
        var navigation = CreateService();

        var navigated = await navigation.NavigateToAsync<FirstViewModel>();

        Assert.True(navigated);
        Assert.IsType<FirstViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task NavigateToAsync_FromCleanState_CannotGoBack()
    {
        var navigation = CreateService();

        await navigation.NavigateToAsync<FirstViewModel>();

        Assert.False(navigation.CanGoBack);
        Assert.False(navigation.CanGoForward);
    }

    [Fact]
    public async Task GoBackAsync_ReturnsToPreviousViewModel()
    {
        var navigation = CreateService();
        await navigation.NavigateToAsync<FirstViewModel>();
        await navigation.NavigateToAsync<SecondViewModel>();

        Assert.True(navigation.CanGoBack);
        var wentBack = await navigation.GoBackAsync();

        Assert.True(wentBack);
        Assert.IsType<FirstViewModel>(navigation.CurrentViewModel);
        Assert.True(navigation.CanGoForward);
    }

    [Fact]
    public async Task GoForwardAsync_ReturnsToTheViewModelWeCameBackFrom()
    {
        var navigation = CreateService();
        await navigation.NavigateToAsync<FirstViewModel>();
        await navigation.NavigateToAsync<SecondViewModel>();
        await navigation.GoBackAsync();

        var wentForward = await navigation.GoForwardAsync();

        Assert.True(wentForward);
        Assert.IsType<SecondViewModel>(navigation.CurrentViewModel);
        Assert.False(navigation.CanGoForward);
    }

    [Fact]
    public async Task NavigateToAsync_AfterGoingBack_ClearsTheForwardStack()
    {
        // Browser semantics: a new destination abandons the forward history.
        var navigation = CreateService();
        await navigation.NavigateToAsync<FirstViewModel>();
        await navigation.NavigateToAsync<SecondViewModel>();
        await navigation.GoBackAsync();

        await navigation.NavigateToAsync<ThirdViewModel>();

        Assert.False(navigation.CanGoForward);
        Assert.IsType<ThirdViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task GoBackAsync_WithEmptyHistory_ReportsFailure()
    {
        var navigation = CreateService();
        await navigation.NavigateToAsync<FirstViewModel>();

        var wentBack = await navigation.GoBackAsync();

        Assert.False(wentBack);
        Assert.IsType<FirstViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task NavigateToAsync_RaisesNavigatedWithBothViewModels()
    {
        var navigation = CreateService();
        await navigation.NavigateToAsync<FirstViewModel>();

        NavigatedEventArgs? captured = null;
        navigation.Navigated += (_, e) => captured = e;

        await navigation.NavigateToAsync<SecondViewModel>();

        Assert.NotNull(captured);
        Assert.IsType<FirstViewModel>(captured!.Previous);
        Assert.IsType<SecondViewModel>(captured.Current);
    }

    [Fact]
    public async Task ClearHistory_KeepsCurrentButDropsBothStacks()
    {
        var navigation = CreateService();
        await navigation.NavigateToAsync<FirstViewModel>();
        await navigation.NavigateToAsync<SecondViewModel>();

        navigation.ClearHistory();

        Assert.False(navigation.CanGoBack);
        Assert.False(navigation.CanGoForward);
        Assert.IsType<SecondViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task RefreshAsync_KeepsTheCurrentDestinationAndHistory()
    {
        var navigation = CreateService();
        await navigation.NavigateToAsync<FirstViewModel>();
        await navigation.NavigateToAsync<SecondViewModel>();

        var refreshed = await navigation.RefreshAsync();

        Assert.True(refreshed);
        Assert.IsType<SecondViewModel>(navigation.CurrentViewModel);

        // Refresh re-runs the destination; it must not stack a duplicate history entry.
        await navigation.GoBackAsync();
        Assert.IsType<FirstViewModel>(navigation.CurrentViewModel);
    }

    [Fact]
    public async Task NavigateToAsync_WithNonViewModelType_Throws()
    {
        var navigation = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => navigation.NavigateToAsync(typeof(string)));
    }
}
