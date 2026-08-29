using System.Text;
using Xunit;

namespace WeighBridge.Tests.App;

/// <summary>
/// Verifies the source contract of SettingsViewModel and SettingsView.xaml to prevent regressions
/// where PortName is cleared or reset during RefreshPorts or ComboBox items-source synchronization.
/// </summary>
public sealed class SettingsViewModelSourceContractTests
{
    private const string ViewModelPath =
        @"..\..\..\..\..\src\WeighBridge.App\ViewModels\SettingsViewModel.cs";

    private const string ViewPath =
        @"..\..\..\..\..\src\WeighBridge.App\Views\SettingsView.xaml";

    private static string ViewModelSource => File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ViewModelPath)));

    private static string ViewSource => File.ReadAllText(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ViewPath)));

    [Fact]
    public void RefreshPorts_Preserves_PortName_Parameter_Or_Property()
    {
        // Must accept explicitPortToSelect or preserve PortName
        Assert.Contains("RefreshPorts(string? explicitPortToSelect = null)", ViewModelSource);
        Assert.Contains("var targetPort = explicitPortToSelect ?? PortName;", ViewModelSource);
        Assert.Contains("PortName = targetPort;", ViewModelSource);
    }

    [Fact]
    public void DetectIndicatorAsync_Passes_Detected_Port_To_RefreshPorts_And_Assigns_PortName()
    {
        Assert.Contains("RefreshPorts(explicitPortToSelect: detectedPort);", ViewModelSource);
        Assert.Contains("PortName = detectedPort;", ViewModelSource);
    }

    [Fact]
    public void SettingsView_Binds_ComboBox_Text_To_PortName_With_PropertyChanged_UpdateSource()
    {
        Assert.Contains("Text=\"{Binding PortName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", ViewSource);
        Assert.DoesNotContain("SelectedItem=\"{Binding PortName", ViewSource);
    }

    [Fact]
    public void SaveConfigurationAsync_Persists_PortName_To_Hardware_Options()
    {
        Assert.Contains("[\"Hardware:WeightIndicator:PortName\"] = PortName", ViewModelSource);
    }
}
