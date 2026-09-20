using System.IO;
using System.Text;
using WeighBridge.Core.Reporting;
using WeighBridge.App.ViewModels;
using Xunit;

namespace WeighBridge.Tests.App;

public sealed class Phase1ModernizationContractTests
{
    private const string SettingsViewPath =
        @"..\..\..\..\..\src\WeighBridge.App\Views\SettingsView.xaml";

    private const string SettingsViewModelPath =
        @"..\..\..\..\..\src\WeighBridge.App\ViewModels\SettingsViewModel.cs";

    private const string VehicleEntryViewPath =
        @"..\..\..\..\..\src\WeighBridge.App\Views\VehicleEntryView.xaml";

    private const string DuplicateSlipViewPath =
        @"..\..\..\..\..\src\WeighBridge.App\Views\DuplicateSlipView.xaml";

    private const string ReportPreviewViewModelPath =
        @"..\..\..\..\..\src\WeighBridge.App\ViewModels\ReportPreviewViewModel.cs";

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath)));

    [Fact]
    public void SettingsView_WbNameFeeding_ContainsOnlyThreeFields()
    {
        var source = Read(SettingsViewPath);

        var startIndex = source.IndexOf("Header=\"WB Name Feeding\"", System.StringComparison.Ordinal);
        Assert.True(startIndex > 0);
        var endIndex = source.IndexOf("Header=\"Appearance\"", startIndex, System.StringComparison.Ordinal);
        Assert.True(endIndex > startIndex);
        var wbTab = source.Substring(startIndex, endIndex - startIndex);

        // Header and weighbridge fields
        Assert.Contains("Text=\"{Binding WeighbridgeName, UpdateSourceTrigger=PropertyChanged}\"", wbTab);
        Assert.Contains("Text=\"{Binding WeighbridgeAddress1, UpdateSourceTrigger=PropertyChanged}\"", wbTab);
        Assert.Contains("Text=\"{Binding WeighbridgeAddress2, UpdateSourceTrigger=PropertyChanged}\"", wbTab);

        // Excluded fields
        Assert.DoesNotContain("Binding Phone", wbTab);
        Assert.DoesNotContain("Binding Email", wbTab);
        Assert.DoesNotContain("Binding TaxId", wbTab);
        Assert.DoesNotContain("Tax ID / GSTIN", wbTab);
    }

    [Fact]
    public void SettingsView_IncludesSignalDiagnosticTab_WithNativeConsoleAndCompanion()
    {
        var source = Read(SettingsViewPath);

        Assert.Contains("Header=\"Signal Diagnostic\"", source);
        Assert.Contains("DiagnosticAsciiLog", source);
        Assert.Contains("StartDiagnosticMonitoringCommand", source);
        Assert.Contains("StopDiagnosticMonitoringCommand", source);
        Assert.Contains("LaunchBraysTerminalCommand", source);
        Assert.Contains("StopBraysTerminalCommand", source);
        Assert.Contains("ToggleTerminalPopOutCommand", source);
    }


    [Fact]
    public void SettingsViewModel_ImplementsTerminalLifecycle_AndPortRelease()
    {
        var source = Read(SettingsViewModelPath);

        Assert.Contains("StartTerminalAsync", source);
        Assert.Contains("StopTerminalAsync", source);
        Assert.Contains("ToggleTerminalPopOut", source);
        Assert.Contains("await _indicator.DisconnectAsync()", source);
        Assert.Contains("await _indicator.ConnectAsync()", source);
    }

    [Fact]
    public void VehicleEntryView_DataGrid_IncludesAll12Attributes_AndScrollbars()
    {
        var source = Read(VehicleEntryViewPath);

        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", source);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", source);

        // Check attributes in WaitingGrid
        Assert.Contains("Header=\"Ticket No.\"", source);
        Assert.Contains("Header=\"Vehicle No\"", source);
        Assert.Contains("Header=\"Vehicle Type\"", source);
        Assert.Contains("Header=\"Party Name\"", source);
        Assert.Contains("Header=\"Charges 1st\"", source);
        Assert.Contains("Header=\"Charges 2nd\"", source);
        Assert.Contains("Header=\"Material\"", source);
        Assert.Contains("Header=\"Gross Weight\"", source);
        Assert.Contains("Header=\"Tare Weight\"", source);
        Assert.Contains("Header=\"Gross Wt Date\"", source);
        Assert.Contains("Header=\"Gross Wt Time\"", source);
        Assert.Contains("Header=\"Tare Wt Date\"", source);
        Assert.Contains("Header=\"Tare Wt Time\"", source);
    }

    [Fact]
    public void DuplicateSlipView_DataGrid_IncludesAll12Attributes_AndScrollbars()
    {
        var source = Read(DuplicateSlipViewPath);

        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", source);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", source);

        // Check attributes in SearchResults DataGrid
        Assert.Contains("Header=\"Ticket No.\"", source);
        Assert.Contains("Header=\"Vehicle No\"", source);
        Assert.Contains("Header=\"Vehicle Type\"", source);
        Assert.Contains("Header=\"Party Name\"", source);
        Assert.Contains("Header=\"Charges 1st\"", source);
        Assert.Contains("Header=\"Charges 2nd\"", source);
        Assert.Contains("Header=\"Material\"", source);
        Assert.Contains("Header=\"Gross Weight\"", source);
        Assert.Contains("Header=\"Tare Weight\"", source);
        Assert.Contains("Header=\"Gross Wt Date\"", source);
        Assert.Contains("Header=\"Gross Wt Time\"", source);
        Assert.Contains("Header=\"Tare Wt Date\"", source);
        Assert.Contains("Header=\"Tare Wt Time\"", source);
    }

    [Fact]
    public void ReportPreviewViewModel_FormatDocumentToText_ProducesExactMonospaceLayout()
    {
        var rows = new[]
        {
            new ReportDocumentRow(
                SerialNumber: 1,
                SlipNumber: "WB-000001",
                VehicleNumber: "MH12AB1234",
                VehicleTypeName: "10 Wheeler",
                PartyName: "Tata Steel",
                MaterialName: "Iron Ore",
                Charges1: 250m,
                Charges2: 50m,
                TotalCharges: 300m,
                GrossWeightKg: 42500m,
                TareWeightKg: 14200m,
                NetWeightKg: 28300m,
                GrossCapturedAtLocal: new DateTime(2026, 9, 18, 10, 30, 0),
                TareCapturedAtLocal: new DateTime(2026, 9, 18, 14, 15, 0),
                CompletedAtLocal: new DateTime(2026, 9, 18, 14, 15, 0),
                Status: "Completed")
        };

        var doc = ReportDocument.Create(
            title: "Daily Weighment Summary",
            companyName: "DURGAPUR WEIGHBRIDGE COMPLEX",
            startDateLocal: new DateTime(2026, 9, 18),
            endDateLocal: new DateTime(2026, 9, 18),
            rows: rows,
            addressLine1: "Plot 12, Industrial Estate Phase 2",
            addressLine2: "Durgapur - 713201");

        var text = ReportPreviewViewModel.FormatDocumentToText(doc);

        // 1. 3 Centered header lines
        Assert.Contains("DURGAPUR WEIGHBRIDGE COMPLEX", text);
        Assert.Contains("Plot 12, Industrial Estate Phase 2", text);
        Assert.Contains("Durgapur - 713201", text);

        // 2. Subheader
        Assert.Contains("Report From Date - 9/18/2026 To Date - 9/18/2026", text);

        // 3. 12 Column headers
        Assert.Contains("S.No", text);
        Assert.Contains("Vehicle No.", text);
        Assert.Contains("Vehicle Type", text);
        Assert.Contains("Party Name", text);
        Assert.Contains("Material", text);
        Assert.Contains("Chg1", text);
        Assert.Contains("Chg2", text);
        Assert.Contains("G Wt.", text);
        Assert.Contains("T Wt.", text);
        Assert.Contains("N Wt.", text);
        Assert.Contains("GWt Date/Time", text);
        Assert.Contains("TWt Date/Time", text);

        // 4. Data Row Content
        Assert.Contains("MH12AB1234", text);
        Assert.Contains("Tata Steel", text);
        Assert.Contains("42500", text);
        Assert.Contains("14200", text);
        Assert.Contains("28300", text);

        // 5. 3-line totals summary block
        Assert.Contains("Total No of Records : 1", text);
        Assert.Contains("Total Net Weight    : 28300 Kg.", text);
        Assert.Contains("Total Charges       : 300 /-", text);
    }

    [Fact]
    public void AllXamlStaticResourceIcons_AreDefinedInIconsXaml()
    {
        var iconsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\WeighBridge.App\Resources\Icons.xaml"));
        var iconsContent = File.ReadAllText(iconsPath);
        var definedIcons = new System.Collections.Generic.HashSet<string>();
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(iconsContent, @"x:Key=""(Icon\.[^""]+)"""))
        {
            definedIcons.Add(match.Groups[1].Value);
        }

        var appViewsDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\WeighBridge.App"));
        var xamlFiles = Directory.GetFiles(appViewsDir, "*.xaml", SearchOption.AllDirectories);

        var missing = new System.Collections.Generic.List<string>();
        foreach (var file in xamlFiles)
        {
            var content = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(content, @"\{StaticResource\s+(Icon\.[A-Za-z0-9_]+)\}"))
            {
                var iconName = match.Groups[1].Value;
                if (!definedIcons.Contains(iconName))
                {
                    missing.Add($"{Path.GetFileName(file)}: {iconName}");
                }
            }
        }

        Assert.Empty(missing);
    }
}
