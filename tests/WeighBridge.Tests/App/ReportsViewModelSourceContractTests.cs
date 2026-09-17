namespace WeighBridge.Tests.App;

public sealed class ReportsViewModelSourceContractTests
{
    private const string ViewModelPath =
        @"..\..\..\..\..\src\WeighBridge.App\ViewModels\ReportsViewModel.cs";

    private static string Source => File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ViewModelPath)));

    [Fact]
    public void OptionalFilters_TreatDashPlaceholdersAsEmptyCriteria()
    {
        Assert.Contains("private static string? NormalizeOptionalFilter(string? value)", Source);
        Assert.Contains("return trimmed is \"-\" or \"–\" or \"—\" ? null : trimmed;", Source);
        Assert.Contains("NormalizeOptionalFilter(StartSlipInput)", Source);
        Assert.Contains("NormalizeOptionalFilter(EndSlipInput)", Source);
    }
}
