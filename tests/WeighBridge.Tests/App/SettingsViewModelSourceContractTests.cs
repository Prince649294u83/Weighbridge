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
    public void TestConnectionAsync_CoordinatesWithSingletonIndicator_WithoutPortProbing()
    {
        Assert.Contains("TestConnectionAsync()", ViewModelSource);
        Assert.Contains("ApplyToOptions();", ViewModelSource);
        Assert.Contains("await _indicator.DisconnectAsync()", ViewModelSource);
        Assert.Contains("await _indicator.ConnectAsync()", ViewModelSource);
        Assert.DoesNotContain("ScanAsync()", ViewModelSource);
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
        Assert.Contains("[\"Hardware:WeightIndicator:BaudRate\"] = BaudRate", ViewModelSource);
        Assert.Contains("ApplyToOptions();", ViewModelSource);
    }

    [Fact]
    public void SettingsView_Exposes_Driver_Port_And_Baud_In_Test_Section()
    {
        Assert.Contains("COM Port Test &amp; Verification", ViewSource);
        Assert.Contains("ItemsSource=\"{Binding DriverTypes}\" SelectedItem=\"{Binding DriverType}\"", ViewSource);
        Assert.Contains("Text=\"{Binding PortName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", ViewSource);
        Assert.Contains("ItemsSource=\"{Binding BaudRates}\" SelectedItem=\"{Binding BaudRate}\"", ViewSource);
    }

    [Fact]
    public void SettingsViewModel_Enforces_Exact_Client_Printer_And_Paper_Options()
    {
        Assert.Contains("Dot Matrix Printer", ViewModelSource);
        Assert.Contains("Graphics Printer", ViewModelSource);
        Assert.Contains("Label / Sticker Printer", ViewModelSource);
        Assert.Contains("Half A4 / A5", ViewModelSource);
        Assert.DoesNotContain("\"Continuous\"", ViewModelSource);
        Assert.DoesNotContain("\"POS\"", ViewModelSource);
    }
}
