using System.Text;

namespace WeighBridge.Tests.App;

/// <summary>
/// The presentation layer targets net8.0-windows and cannot be referenced from this
/// headless test project, so defects there have no ordinary unit-test net. These tests pin
/// the two most expensive regressions at source level: the Print Slip command that never
/// refreshed its CanExecute state, and the culture mismatch between how indicator weights
/// were written and parsed. They are text assertions on purpose — if either pattern is
/// removed again without an equivalent fix, these tests fail loudly.
/// </summary>
public sealed class VehicleEntrySourceContractTests
{
    private const string ViewModelPath =
        @"..\..\..\..\..\src\WeighBridge.App\ViewModels\VehicleEntryViewModel.cs";

    private static string Source => File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ViewModelPath)));

    [Fact]
    public void RefreshCommandStates_NotifiesThePrintSlipCommand()
    {
        var fromMethod = Source[Source.IndexOf("private void RefreshCommandStates()")..];
        Assert.True(fromMethod.Length > 0, "RefreshCommandStates() must exist.");
        var body = fromMethod[..fromMethod.IndexOf('}', fromMethod.IndexOf("_refresh.NotifyCanExecuteChanged()"))];

        Assert.Contains("_printSlip.NotifyCanExecuteChanged()", body);
    }

    [Fact]
    public void WeightParsing_AcceptsBothCurrentAndInvariantCulture()
    {
        Assert.Contains("CultureInfo.CurrentCulture", Source);
        Assert.Contains("CultureInfo.InvariantCulture", Source);
    }
}
