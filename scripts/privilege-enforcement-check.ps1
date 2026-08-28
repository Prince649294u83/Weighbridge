# Runtime proof for the two reachable privilege gaps found in the phase 17 review:
#
#   Weighment.Reprint  - the Duplicate Slip module is on the navigation rail for every role,
#                        and nothing consulted the permission, so any signed-in account could
#                        reissue any slip. Now enforced in WindowsPrintService.PrintAsync.
#   Reports.Export     - the Reports module is on the rail for every role and Generate CSV
#                        wrote a file containing the site's whole weighment history. Now
#                        enforced in CsvReportService.GenerateAsync.
#
# Neither fix can be unit-tested: WeighBridge.Printing and WeighBridge.App target
# net8.0-windows and the test project targets net8.0, so it cannot reference them. The CSV
# fix has unit tests because WeighBridge.Reporting is net8.0. This script is the only
# evidence for the print gate, and it checks both from the outside either way.
#
# Both directions are checked in one run, which is the point: a refusal that also refuses a
# privileged operator is a broken feature, not a control.
#
#   as admin (holds both)          Generate CSV succeeds, Reprint is not refused for privilege
#   as a ReadOnly account          both are refused by name, and no CSV appears on disk
#
# Printer.Enabled is false in the shipped defaults, and the temporary root gets those defaults,
# so the privileged reprint returns "Printing is disabled." - which is the discriminator this
# needs and prints no paper. The check is that the message is NOT about permission.
#
# Real UIA only, no direct view-model access. Runs against its own temporary data root, so it
# weighs the vehicle the reprint check needs rather than requiring one to be there already, and
# both the probe account and the database it lives in are gone when the run ends.
#
# Exit 0 = every check held. Exit 1 = at least one failed. Exit 2 = could not complete.

$ErrorActionPreference = 'Stop'

# Isolated data root: this run must never touch the operator's live database.
. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$data = New-IsolatedDataRoot -Label 'privilege-enforcement-check'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'
$logFile = $data.LogFile
$reportsDir = $data.ReportDir

$probeUser = 'audit_readonly'
# Generated per run rather than written down: a literal here reads as a credential even though
# it only ever unlocks an account this script created in a directory it is about to delete.
$probePassword = -join ((1..16) | ForEach-Object { [char](Get-Random -InputObject (@(48..57) + @(65..90) + @(97..122))) })

function Say($text) { Write-Host "[privilege] $text" }

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'login-helper.ps1')

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class PrivClick {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }
}
'@

$UIA = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
$Ctrl = [System.Windows.Automation.ControlType]
$baseline = if (Test-Path $logFile) { (Get-Item $logFile).Length } else { 0 }
$failures = @()

# A function that did `$failures += ...` would create a local copy and drop the failure,
# which is exactly the shape of check that passes for the wrong reason.
function Add-Failure($text) { $script:failures += $text }

function New-Condition($property, $value) {
    return New-Object System.Windows.Automation.PropertyCondition($property, $value)
}

function Find-ByNameAndType($scope, $name, $type) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Condition $UIA::NameProperty $name),
        (New-Condition $UIA::ControlTypeProperty $type))
    return $scope.FindFirst($Tree::Descendants, $cond)
}

function Invoke-Named($scope, $name, $type) {
    $element = Find-ByNameAndType $scope $name $type
    if (-not $element) { throw "no $($type.ProgrammaticName) named '$name'" }
    if (-not $element.Current.IsEnabled) { throw "the control named '$name' is disabled" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 500
}

function Get-RunLog {
    if (-not (Test-Path $logFile)) { return '' }
    $stream = [System.IO.File]::Open($logFile, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($baseline, 'Begin') | Out-Null
        return (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    } finally { $stream.Dispose() }
}

function Get-ScreenText($scope) {
    $texts = @()
    foreach ($item in $scope.FindAll($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::Text))) {
        $texts += $item.Current.Name
    }
    return $texts
}

# Navigation is confirmed by a delta in the log, never by the rail highlight: the rail runs
# its navigation off Click, so a ticked radio button proves nothing about what is displayed.
function Open-Module($shell, $label, $viewModel) {
    $item = Find-ByNameAndType $shell $label $Ctrl::RadioButton
    if (-not $item) { return "the navigation rail offers no '$label' item" }

    [WeighBridgeFocus]::SetForegroundWindow([IntPtr]$shell.Current.NativeWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 300

    foreach ($attempt in 1, 2) {
        $before = ([regex]::Matches((Get-RunLog), "Navigated to $viewModel")).Count
        try {
            $point = $item.GetClickablePoint()
            [PrivClick]::Click([int]$point.X, [int]$point.Y)
        } catch {
            return "'$label' has no clickable point: $($_.Exception.Message)"
        }
        $deadline = (Get-Date).AddSeconds(6)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
            if (([regex]::Matches((Get-RunLog), "Navigated to $viewModel")).Count -gt $before) {
                Start-Sleep -Milliseconds 400
                return $null
            }
        }
        if ($attempt -eq 1) { Say "  retrying '$label' (the first click produced no navigation)" }
    }
    return "clicked '$label' twice with no new 'Navigated to $viewModel' line"
}

# Waits for the module's status line to say something, then returns it. Both modules bind a
# single TextBlock to StatusMessage inside a Border that is collapsed while empty, so the new
# text is whatever appeared that was not there before the click.
function Get-StatusAfter($shell, $before, $timeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $new = @(Get-ScreenText $shell | Where-Object { $_ -and $_.Trim() -and $before -notcontains $_ })
        if ($new.Count -gt 0) { return ($new -join ' | ') }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Test-Reports($shell, $who, [switch]$ExpectRefusal) {
    $problem = Open-Module $shell 'Reports' 'ReportsViewModel'
    if ($problem) { Add-Failure "${who}: reports: $problem"; return }

    $csvBefore = @(if (Test-Path $reportsDir) { Get-ChildItem $reportsDir -Filter '*.csv' } else { @() })
    $before = Get-ScreenText $shell
    Invoke-Named $shell 'Generate CSV' $Ctrl::Button
    $status = Get-StatusAfter $shell $before
    if (-not $status) { Add-Failure "${who}: Generate CSV produced no status message"; return }
    Say "  ${who}: Generate CSV said: $status"

    $csvAfter = @(if (Test-Path $reportsDir) { Get-ChildItem $reportsDir -Filter '*.csv' } else { @() })
    $written = $csvAfter.Count - $csvBefore.Count

    if ($ExpectRefusal) {
        if ($status -notmatch 'permission') {
            Add-Failure "${who}: Generate CSV was not refused for permission; it said '$status'"
        }
        if ($written -ne 0) {
            Add-Failure "${who}: Generate CSV was refused but still wrote $written file(s) to $reportsDir"
        } else {
            Say "  ${who}: no file was written"
        }
    } else {
        if ($status -match 'permission') {
            Add-Failure "${who}: Generate CSV was refused for permission but this role holds Reports.Export"
        }
        if ($written -lt 1) {
            Add-Failure "${who}: Generate CSV reported '$status' but wrote no file to $reportsDir"
        } else {
            Say "  ${who}: wrote $written file(s)"
        }
    }
}

function Test-Reprint($shell, $who, [switch]$ExpectRefusal) {
    $problem = Open-Module $shell 'Duplicate Slip' 'DuplicateSlipViewModel'
    if ($problem) { Add-Failure "${who}: duplicate slip: $problem"; return }

    Invoke-Named $shell 'Search' $Ctrl::Button
    Start-Sleep -Seconds 1

    $grid = Find-ByNameAndType $shell 'Completed weighments' $Ctrl::DataGrid
    if (-not $grid) { Add-Failure "${who}: the completed-weighments grid is not present"; return }

    $rows = $grid.FindAll($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::DataItem))
    if ($rows.Count -lt 1) {
        Add-Failure "${who}: the search returned no completed weighment for today, so reprint cannot be exercised"
        return
    }

    # SelectionItemPattern is correct here, unlike on the rail: the grid's SelectedItem is
    # data-bound to SelectedWeighment, so selecting the row is what the operator's click does.
    $rows[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 600

    $reprint = Find-ByNameAndType $shell 'Reprint slip' $Ctrl::Button
    if (-not $reprint) { Add-Failure "${who}: no 'Reprint slip' button"; return }
    if (-not $reprint.Current.IsEnabled) {
        Add-Failure "${who}: 'Reprint slip' stayed disabled after a row was selected"
        return
    }

    $before = Get-ScreenText $shell
    $reprint.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $status = Get-StatusAfter $shell $before
    if (-not $status) { Add-Failure "${who}: Reprint slip produced no status message"; return }
    Say "  ${who}: Reprint slip said: $status"

    if ($ExpectRefusal) {
        if ($status -notmatch 'permission') {
            Add-Failure "${who}: the reprint was not refused for permission; it said '$status'"
        }
    } else {
        if ($status -match 'permission') {
            Add-Failure "${who}: the reprint was refused for permission but this role holds Weighment.Reprint"
        }
    }
}

function Stop-App($shell, $process) {
    try { $shell.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
    if (-not $process.WaitForExit(15000)) { $process.Kill() }
}

# The user editor sets no AutomationProperties.Name on its inputs. The 'Account is Active'
# check box is unique and shares their parent, so it anchors the panel; the boxes are then
# taken in tree order: Username, Display Name, Password.
function New-ReadOnlyUser($shell) {
    Invoke-Named $shell '+ Add User' $Ctrl::Button

    $anchor = Find-ByNameAndType $shell 'Account is Active' $Ctrl::CheckBox
    if (-not $anchor) { throw 'the user editor panel did not open' }
    $panel = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($anchor)
    $boxes = $panel.FindAll($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::Edit))
    if ($boxes.Count -lt 3) { throw "the editor panel exposes $($boxes.Count) text boxes, expected 3" }

    foreach ($pair in @(@($boxes[0], $probeUser), @($boxes[1], 'Audit Read Only Probe'), @($boxes[2], $probePassword))) {
        $pair[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($pair[1])
        Start-Sleep -Milliseconds 150
    }

    $combo = $panel.FindFirst($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::ComboBox))
    if (-not $combo) { throw 'the editor panel has no role combo box' }
    $combo.SetFocus()
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait('R')   # the four shipped roles have four distinct initials
    Start-Sleep -Milliseconds 400

    # Read back rather than assume: a keystroke that picked the wrong role would create the
    # probe at the wrong privilege level and invalidate the whole run.
    $selection = $combo.GetCurrentPattern(
        [System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selection.Count -lt 1 -or $selection[0].Current.Name -notlike 'ReadOnly*') {
        throw "the role combo box selected '$(if ($selection.Count) { $selection[0].Current.Name } else { 'nothing' })', not ReadOnly"
    }

    foreach ($label in 'Create', 'Save Changes') {
        if (Find-ByNameAndType $shell $label $Ctrl::Button) {
            Invoke-Named $shell $label $Ctrl::Button
            Start-Sleep -Seconds 1
            return
        }
    }
    throw 'the editor panel has no save button'
}

function Get-GridUsernames($shell) {
    $grid = $shell.FindFirst($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::DataGrid))
    if (-not $grid) { return @() }
    $names = @()
    foreach ($cell in $grid.FindAll($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::Text))) {
        $names += $cell.Current.Name
    }
    return $names
}

function Set-Field($scope, $name, $value) {
    $field = Find-ByNameAndType $scope $name $Ctrl::Edit
    if (-not $field) { throw "no text box named '$name'" }
    $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
    Start-Sleep -Milliseconds 150
}

# The reprint gate needs something to reprint. This used to be the caller's problem - the
# header told whoever ran it to run vehicle-entry-smoke.ps1 first against the live database -
# which is no longer possible and never was safe: the isolated root starts with an empty
# database every time. Weighing one vehicle here costs eight interactions and removes the
# precondition entirely, and a run that cannot produce the row fails rather than skipping the
# check it exists to make.
function New-CompletedWeighment($shell) {
    $problem = Open-Module $shell 'Vehicle Entry' 'VehicleEntryViewModel'
    if ($problem) { throw "vehicle entry: $problem" }

    Set-Field $shell 'Vehicle number' 'MH12PR0001'
    Set-Field $shell 'Party' 'Privilege Check Party'
    Set-Field $shell 'Material' 'Clinker'
    Invoke-Named $shell 'Open weighment' $Ctrl::Button

    Set-Field $shell 'Weight in kilograms' '32500'
    Invoke-Named $shell 'Record first weight' $Ctrl::Button

    Set-Field $shell 'Weight in kilograms' '12250'
    Invoke-Named $shell 'Record second weight' $Ctrl::Button

    # Read back rather than assume: a weighment left at 'Awaiting second weight' is not
    # reprintable, and the reprint check would then fail for the wrong reason.
    if ((Get-ScreenText $shell) -notmatch '^Completed$') {
        throw 'the seed weighment did not reach the Completed stage'
    }
    Say '  seeded one completed weighment for the reprint check'
}

$proc = $null
try {
    # --- as the administrator: the privileged paths must still work ---------------------
    Say 'phase 1: as the administrator (holds Reports.Export and Weighment.Reprint)'
    $proc = Start-Process -FilePath $exe -PassThru
    $shell = Invoke-WeighBridgeLogin -ProcessId $proc.Id -TimeoutSec 45

    New-CompletedWeighment $shell

    Test-Reports $shell 'admin'
    Test-Reprint $shell 'admin'

    Say "creating the '$probeUser' probe account"
    $problem = Open-Module $shell 'Administration' 'AdministrationViewModel'
    if ($problem) { throw "administration: $problem" }
    # No reuse branch: the data root is new every run, so an account of this name already
    # existing would mean the isolation is not in effect - and its password would not be the
    # one generated above, so phase 2 would fail on the login rather than on a privilege.
    if ((Get-GridUsernames $shell) -contains $probeUser) {
        throw "'$probeUser' already exists in a data root that should be empty - isolation is not in effect"
    }
    New-ReadOnlyUser $shell
    if ((Get-GridUsernames $shell) -notcontains $probeUser) {
        throw "'$probeUser' was not created - it is not listed in the users grid"
    }
    Say "  created '$probeUser' with role ReadOnly"

    Stop-App $shell $proc

    # --- as a ReadOnly account: both operations must be refused by name ----------------
    Say "phase 2: as '$probeUser' (holds neither permission)"
    $proc = Start-Process -FilePath $exe -PassThru
    $shell = Invoke-WeighBridgeLogin -ProcessId $proc.Id -Username $probeUser -Password $probePassword -TimeoutSec 45

    Test-Reports $shell $probeUser -ExpectRefusal
    Test-Reprint $shell $probeUser -ExpectRefusal

    Stop-App $shell $proc

    # The refusals are recorded, not just shown: a control that only tells the operator
    # leaves nothing behind for whoever reads the log afterwards.
    $log = Get-RunLog
    foreach ($expected in 'lacks Reports.Export', 'lacks Weighment.Reprint') {
        if ($log -notmatch [regex]::Escape($expected)) {
            Add-Failure "the log has no '$expected' line for the refused attempt"
        } else {
            Say "  log records: $expected"
        }
    }
}
catch {
    Write-Host ''
    Write-Host "[privilege] INCONCLUSIVE: $($_.Exception.Message)" -ForegroundColor Yellow
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }
    exit 2
}
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); Say "killed leftover process $($proc.Id)" }
    Remove-IsolatedDataRoot -Root $data.Root
}

Write-Host ''
if ($failures.Count -eq 0) {
    Say 'PASS: both operations succeed for a privileged operator and are refused, by name and in the log, for one who is not.'
    exit 0
}

Say '--- FAILED ---'
foreach ($failure in $failures) { Say "  $failure" }
exit 1
