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

    [Fact]
    public void SubmitWorkflow_IsEnabled_For_F1_And_F2_Starter_States()
    {
        Assert.Contains("WeighmentWorkflowState.F1Entry => CanCreate", Source);
        Assert.Contains("WeighmentWorkflowState.F2Entry => CanEdit", Source);
    }

    [Fact]
    public void VehicleEntry_Defaults_Back_To_F1Ready_For_Operator_Flow()
    {
        Assert.Contains("private WeighmentWorkflowState _workflowState = WeighmentWorkflowState.F1Entry;", Source);
        Assert.Contains("private void EnsureReadyForFirstEntry()", Source);
        Assert.Contains("WorkflowState = IsOnlySingleEntryEnabled ? WeighmentWorkflowState.Completed : WeighmentWorkflowState.F1Entry;", Source);
    }

    [Fact]
    public void VehicleType_Box_Allows_Typed_Entry_While_Keeping_Master_Selection()
    {
        Assert.Contains("public string? SelectedVehicleTypeName", Source);
        Assert.Contains("IsEditable=\"True\"", File.ReadAllText(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\WeighBridge.App\Views\VehicleEntryView.xaml"))));
        Assert.Contains("Text=\"{Binding SelectedVehicleTypeName, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", File.ReadAllText(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\WeighBridge.App\Views\VehicleEntryView.xaml"))));
    }

    [Fact]
    public void F1_Workflow_Allocates_Immediate_Ticket_Reservation()
    {
        Assert.Contains("public async Task EnsureReservationAsync()", Source);
        Assert.Contains("new ReserveTicketCommand(_weighments", Source);
        Assert.Contains("ActiveReservationId = reservation.Id;", Source);
        Assert.Contains("ActiveSlipNumber = reservation.SlipNumber;", Source);
    }

    [Fact]
    public void F1_FirstWeight_Uses_Atomic_Reservation_Command()
    {
        Assert.Contains("new RecordFirstWeightWithReservationCommand(", Source);
        Assert.Contains("ActiveReservationId.Value", Source);
    }

    [Fact]
    public void F2_Historical_Hydration_Populates_All_F1_Snapshot_Fields_And_Isolates_Remarks()
    {
        Assert.Contains("PartyName = weighment.PartyName;", Source);
        Assert.Contains("MaterialName = weighment.MaterialName;", Source);
        Assert.Contains("SelectedArrivalMode = ArrivalModes.FirstOrDefault(m => m.Value == weighment.Mode)", Source);
        Assert.Contains("F2Remarks = null;", Source);
    }

    [Fact]
    public void ClearContext_Cancels_Unconsumed_Ticket_Reservation()
    {
        Assert.Contains("new CancelReservationCommand(_weighments, ActiveReservationId.Value", Source);
    }

    [Fact]
    public void ModePresentation_Supports_Gross_Tare_Auto_And_Locks_In_F2()
    {
        Assert.Contains("public string ModeLabelText => IsF2Mode", Source);
        Assert.Contains("public string F2DerivedModeText", Source);
        Assert.Contains("public bool IsGrossFirstSelected", Source);
        Assert.Contains("public bool IsTareFirstSelected", Source);
        Assert.Contains("public bool IsAutoTareModeSelected", Source);
        Assert.Contains("public bool IsManualTareModeSelected", Source);
        Assert.Contains("public bool IsTareWeightReadOnly => IsF2Mode || _isAutoTareMode || (!_isManualTareMode && !CanEnterManualWeight);", Source);
        Assert.Contains("if (IsF2Mode)", Source);
        Assert.Contains("return Current?.Mode == WeighmentMode.GrossFirst ? \"T\" : \"G\";", Source);

        var viewSource = File.ReadAllText(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\WeighBridge.App\Views\VehicleEntryView.xaml")));
        Assert.Contains("Text=\"{Binding ModeLabelText}\"", viewSource);
        Assert.Contains("AutomationProperties.Name=\"Select Gross Mode\"", viewSource);
        Assert.Contains("AutomationProperties.Name=\"Select Tare Mode\"", viewSource);
        Assert.Contains("AutomationProperties.Name=\"Select Auto Tare Mode\"", viewSource);
        Assert.Contains("AutomationProperties.Name=\"Select Manual Tare Mode\"", viewSource);
        Assert.Contains("AutomationProperties.Name=\"F2 Derived Mode\"", viewSource);
        Assert.Contains("IsReadOnly=\"{Binding IsTareWeightReadOnly}\"", viewSource);
    }

    [Fact]
    public void WeightInputs_AreDecoupled_AndModeButtonsProperlyStyled()
    {
        // 1. Separate backing fields for Gross and Tare inputs
        Assert.Contains("private string _grossWeightInput = string.Empty;", Source);
        Assert.Contains("private string _tareWeightInput = string.Empty;", Source);

        // 2. GrossWeightText and TareWeightText setters assign to their own fields
        Assert.Contains("_grossWeightInput = value;", Source);
        Assert.Contains("_tareWeightInput = value;", Source);

        // 3. View XAML uses valid theme brush and binds TextBlock foreground
        var viewSource = File.ReadAllText(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\src\WeighBridge.App\Views\VehicleEntryView.xaml")));

        Assert.Contains("Brush.Accent.Default", viewSource);
        Assert.Contains("RelativeSource AncestorType=Button", viewSource);
        Assert.DoesNotContain("Brush.Primary.Default", viewSource);
    }
}

