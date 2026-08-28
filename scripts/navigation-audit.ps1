# Navigation audit (phase 7) and the Settings module's editable surface (finding F-006).
#
# Phase 7 asks for every module to be navigated to at least three times, and says not to
# trust the existing navigation smoke script. runtime-smoke.ps1 clicks each module once,
# inside a run that also toggles the theme, refreshes the status bar, takes a screenshot and
# sleeps 35 s for a background tick - so a navigation defect that only appears on the second
# or third visit cannot be seen by it, and a phase-7 run through it would cost 90 s of
# unrelated work. This script does one thing.
#
# Every navigation is confirmed by a *delta* in the count of 'Navigated to XViewModel' lines
# in this run's log, never by the rail highlight: SelectionItemPattern.Select() sets
# IsChecked without running the navigation command, so a rail-highlight assertion passes on a
# module that never loaded. Ratcheting the count also survives the shell restoring a module
# at startup, which makes 'the log mentions this module' true before any click happens.
#
# While Settings is displayed the window's interactive controls are enumerated, which is the
# runtime check on the F-006 repair ('Settings.Edit is declared but enforced nowhere'). The
# shell's own chrome contains no text box, combo box or check box, so anything found belongs
# to the Settings module. This assertion was inverted when the repair landed: it used to
# require zero text inputs, because at the time every hardware value was a read-only
# TextBlock and the finding stood as written.
#
# The last pass revisits two modules repeatedly and samples the process's working set. That
# figure is REPORTED, NOT ASSERTED: working set is noisy, the garbage collector cannot be
# driven from outside the process, and a threshold on it would be exactly the kind of
# assertion that passes or fails for reasons unrelated to the defect. It is corroboration for
# a leak identified in the source, not the evidence for one.
#
# Real mouse clicks are synthesised, because the rail's navigation runs off Click. That moves
# the physical cursor and needs the window in the foreground - do not use the machine while
# this runs.
#
# Runs against its own temporary data root, so there is no live database to move aside and no
# preferences file to restore (finding F-012): the run creates its own database and its own
# first-run administrator, and the whole root is deleted afterwards.
#
# Exit 0 = every assertion held. Exit 1 = at least one failed.

param([int]$Rounds = 3, [int]$LeakVisits = 12, [string]$ExePath)

$ErrorActionPreference = 'Stop'

# Isolated data root replaces the old stash-the-live-DB dance: nothing here can
# touch the operator's database any more.
. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$data = New-IsolatedDataRoot -Label 'navigation-audit'
$projectRoot = Split-Path -Parent $PSScriptRoot
# ExePath lets the same audit run against the published Release build (phase 23) rather than
# only the Debug binary. Named ExePath because $Exe and $exe are one variable in PowerShell.
$exe = if ($ExePath) { $ExePath } else {
    Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'
}
$logFile = $data.LogFile

function Say($text) { Write-Host "[nav] $text" }

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe" }
if ($Rounds -lt 3) { Write-Error "phase 7 requires at least 3 rounds; $Rounds was requested" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'login-helper.ps1')

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NavWin32 {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
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

$element = [System.Windows.Automation.AutomationElement]
$tree = [System.Windows.Automation.TreeScope]

# Opened with FileShare.ReadWrite because the application still holds the file open.
function Get-RunLog($from) {
    if (-not (Test-Path $logFile)) { return '' }
    $stream = [System.IO.File]::Open($logFile, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($from, 'Begin') | Out-Null
        return (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    } finally { $stream.Dispose() }
}

function Get-NavCount($from, $viewModel) {
    return ([regex]::Matches((Get-RunLog $from), "Navigated to $viewModel")).Count
}

function Find-ByNameAndType($scope, $name, $type) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($element::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition($element::ControlTypeProperty, $type)))
    return $scope.FindFirst($tree::Descendants, $cond)
}

function Count-ByType($scope, $type) {
    return @($scope.FindAll($tree::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($element::ControlTypeProperty, $type)))).Count
}

# Selects $value in whichever combo box offers it, and reports whether it succeeded.
#
# Identified by the value it offers rather than by name or position: the Settings combo boxes
# carry no AutomationProperties.Name (finding F-009), and "the second combo box" breaks the
# first time a row is added above it. WPF virtualises the items away until the drop-down is
# open, so it has to be expanded before the item exists in the tree at all.
function Select-ComboValue($scope, $value) {
    foreach ($combo in @($scope.FindAll($tree::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            $element::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox))))) {

        $expand = $null
        try {
            $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        } catch { continue }

        $expand.Expand()
        Start-Sleep -Milliseconds 250

        $item = Find-ByNameAndType $combo $value ([System.Windows.Automation.ControlType]::ListItem)
        if (-not $item) {
            $expand.Collapse()
            continue
        }

        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 400
        return $true
    }

    return $false
}

# Clicks a rail item and waits for the navigation the click is supposed to cause. One retry:
# a synthesised click is occasionally eaten by the compositor, and the retry separates lost
# input from a navigation that genuinely never fires.
function Invoke-Navigation($root, $from, $label, $viewModel) {
    $item = $null
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and -not $item) {
        $item = Find-ByNameAndType $root $label ([System.Windows.Automation.ControlType]::RadioButton)
        if (-not $item) { Start-Sleep -Milliseconds 200 }
    }
    if (-not $item) { return "no rail item named '$label'" }

    foreach ($attempt in 1, 2) {
        $before = Get-NavCount $from $viewModel
        try {
            $point = $item.GetClickablePoint()
            [NavWin32]::Click([int]$point.X, [int]$point.Y)
        } catch {
            return "'$label' has no clickable point: $($_.Exception.Message)"
        }

        $waitUntil = (Get-Date).AddSeconds(6)
        while ((Get-Date) -lt $waitUntil) {
            Start-Sleep -Milliseconds 150
            if ((Get-NavCount $from $viewModel) -gt $before) { return $null }
        }
        if ($attempt -eq 1) { Say "  retrying '$label' (the first click produced no navigation)" }
    }

    return "clicked '$label' twice with no new 'Navigated to $viewModel' line"
}

$modules = [ordered]@{
    'Dashboard'      = 'DashboardViewModel'
    'Vehicle Entry'  = 'VehicleEntryViewModel'
    'Duplicate Slip' = 'DuplicateSlipViewModel'
    'Reports'        = 'ReportsViewModel'
    'Masters'        = 'MastersViewModel'
    'Settings'       = 'SettingsViewModel'
    'Administration' = 'AdministrationViewModel'
}

$failures = @()
$process = $null
$samples = @()

try {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force

    # The live-weight check below needs an indicator that produces readings, so it asks for the
    # simulator by name. It used to work without asking, because "Simulator" was the product's
    # default DriverType - the defect the audit found, since a fresh installation then weighed
    # vehicles with generated numbers. The default is now "Serial", and on a machine with
    # nothing on COM1 no reading ever arrives, which would fail this check for a reason that has
    # nothing to do with navigation.
    #
    # Written straight into the temporary root before the first launch. The provisioner merges
    # in every key this omits, so only the two that matter are stated - and because the root is
    # temporary, there is nothing to back up and nothing to restore.
    [System.IO.File]::WriteAllText($data.ConfigFile, (@{
        Hardware = @{ WeightIndicator = @{ Enabled = $true; DriverType = 'Simulator' } }
    } | ConvertTo-Json -Depth 10))
    Say 'weight indicator set to Simulator for the run'

    $baseline = 0

    $process = Start-Process -FilePath $exe -PassThru
    Say "launched pid=$($process.Id)"

    $root = Invoke-WeighBridgeLogin -ProcessId $process.Id -TimeoutSec 60
    Say "shell up: '$($root.Current.Name)'"

    # Synthesised input goes to whatever window has focus, and a clickable point outside the
    # visible area is not clickable at all, so the window is raised and maximised first.
    [NavWin32]::SetForegroundWindow($root.Current.NativeWindowHandle) | Out-Null
    try {
        $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState(
            [System.Windows.Automation.WindowVisualState]::Maximized)
    } catch { }
    Start-Sleep -Milliseconds 1200

    # --- phase 7: every module, $Rounds times ------------------------------------------
    for ($round = 1; $round -le $Rounds; $round++) {
        Say "round $round of $Rounds"
        foreach ($label in $modules.Keys) {
            $problem = Invoke-Navigation $root $baseline $label $modules[$label]
            if ($problem) { $failures += "round ${round}: $problem" }
        }
    }

    # Counted from the log at the end as well as inline, because the inline check only proves
    # each click caused *a* navigation; this proves the total per module is what was asked for.
    foreach ($label in $modules.Keys) {
        $viewModel = $modules[$label]
        $count = Get-NavCount $baseline $viewModel
        if ($count -lt $Rounds) {
            $failures += "$label was navigated to $count time(s), expected at least $Rounds"
        }
        Say ("  {0,-15} {1} navigation(s)" -f $label, $count)
    }

    # --- F-006: what can actually be changed in Settings -------------------------------
    $problem = Invoke-Navigation $root $baseline 'Settings' 'SettingsViewModel'
    if ($problem) {
        $failures += "settings inspection: $problem"
    } else {
        Start-Sleep -Milliseconds 400
        $edits = Count-ByType $root ([System.Windows.Automation.ControlType]::Edit)
        $combos = Count-ByType $root ([System.Windows.Automation.ControlType]::ComboBox)
        $checks = Count-ByType $root ([System.Windows.Automation.ControlType]::CheckBox)
        Say "settings module: $edits text input(s), $combos combo box(es), $checks check box(es)"

        # The claim now under test is the opposite of the original finding: an operator must be
        # able to change hardware, printing, camera and reporting settings from this screen -
        # the COM port and the baud rate among them. Counts are asserted as lower bounds, not
        # exact numbers: an exact count fails whenever a field is added, which is a change to
        # the screen and not a defect in it.
        if ($edits -lt 1) {
            $failures += 'Settings shows no text input; the hardware values are not editable and F-006 has regressed'
        }
        if ($combos -lt 2) {
            $failures += "Settings shows $combos combo box(es); expected at least the theme and the driver"
        }
        if ($checks -lt 2) {
            $failures += "Settings shows $checks check box(es); expected at least collapsed navigation and the indicator toggle"
        }

        # Named buttons rather than field counts for the paths that must exist: the inputs
        # carry no AutomationProperties.Name (finding F-009) so they cannot be identified
        # individually, but a screen full of text boxes with no way to persist or detect would
        # satisfy the counts above while being useless.
        if (-not (Find-ByNameAndType $root 'Save Configuration' ([System.Windows.Automation.ControlType]::Button))) {
            $failures += "Settings has no 'Save Configuration' button"
        }

        # The serial-port controls are the ones the operator uses to find a physically connected
        # indicator, so their presence is the assertion that matters most on this screen - but
        # they are deliberately hidden unless the driver is Serial, and this run configured the
        # Simulator. Asserting them unconditionally therefore failed against a correct build.
        #
        # Both branches are checked instead. Absence under Simulator is not enough on its own -
        # a build with the detection button deleted outright would satisfy it - so the driver is
        # switched to Serial and the buttons are then required to appear. That covers the
        # conditional visibility itself, which nothing else here did.
        $serialOnly = 'Refresh', 'Detect Indicator'

        foreach ($label in $serialOnly) {
            if (Find-ByNameAndType $root $label ([System.Windows.Automation.ControlType]::Button)) {
                $failures += "Settings shows the serial-only '$label' button while the driver is Simulator"
            }
        }

        if (-not (Select-ComboValue $root 'Serial')) {
            $failures += 'Settings offers no driver combo box containing "Serial"'
        }
        else {
            foreach ($label in $serialOnly) {
                if (-not (Find-ByNameAndType $root $label ([System.Windows.Automation.ControlType]::Button))) {
                    $failures += "Settings has no '$label' button once the driver is Serial"
                }
            }

            # Put it back, so the live-weight check below still faces the simulator it needs.
            # Nothing is saved - the change lives in the ViewModel until Save Configuration is
            # pressed, which this never does.
            if (-not (Select-ComboValue $root 'Simulator')) {
                $failures += 'the driver combo box would not go back to "Simulator"'
            }
        }
    }

    # --- the Dashboard's live-weight subscription --------------------------------------
    # The Dashboard subscribes to the weight indicator on activation and releases it on the
    # way out, so that a visit does not leave a permanent subscriber behind on a singleton
    # that raises a reading every 250 ms. That release is only safe if the subscription is
    # genuinely re-taken on the next visit, and nothing else here would notice if it were not:
    # every navigation assertion above would still pass with a Dashboard that shows a dead
    # reading forever.
    #
    # 'Disconnected' is the field's initial value, so this asserts the status has moved off it
    # to one of the four the reading handler sets. Vehicle Entry is visited first because it
    # is the module that connects the indicator; the Dashboard never connects it itself.
    $problem = Invoke-Navigation $root $baseline 'Vehicle Entry' 'VehicleEntryViewModel'
    if ($problem) { $failures += "before the live-weight check: $problem" }

    $problem = Invoke-Navigation $root $baseline 'Dashboard' 'DashboardViewModel'
    if ($problem) {
        $failures += "live-weight check: $problem"
    } else {
        $status = $null
        $deadline = (Get-Date).AddSeconds(10)
        while ((Get-Date) -lt $deadline) {
            $status = @($root.FindAll($tree::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    $element::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) |
                ForEach-Object { $_.Current.Name } |
                Where-Object { $_ -in 'ZERO', 'NEGATIVE', 'STABLE', 'UNSTABLE' }) | Select-Object -First 1
            if ($status) { break }
            Start-Sleep -Milliseconds 250
        }

        if ($status) {
            Say "dashboard live status: $status"
        } else {
            $failures += 'the Dashboard showed no live indicator status within 10s of being activated'
        }
    }

    # --- retention corroboration: revisit two modules and watch the working set --------
    $process.Refresh()
    $samples += [pscustomobject]@{ At = 'after the rounds'; WorkingSetMb = [int]($process.WorkingSet64 / 1MB); Handles = $process.HandleCount }

    for ($visit = 1; $visit -le $LeakVisits; $visit++) {
        foreach ($label in 'Dashboard', 'Reports') {
            $problem = Invoke-Navigation $root $baseline $label $modules[$label]
            if ($problem) { $failures += "leak pass visit ${visit}: $problem" }
        }
    }

    $process.Refresh()
    $samples += [pscustomobject]@{ At = "after $LeakVisits more visits each"; WorkingSetMb = [int]($process.WorkingSet64 / 1MB); Handles = $process.HandleCount }

    # --- close and read the log --------------------------------------------------------
    [NavWin32]::PostMessage([IntPtr]$root.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    if ($process.WaitForExit(20000)) {
        if ($process.ExitCode -ne 0) { $failures += "exit code $($process.ExitCode)" }
    } else {
        $failures += 'did not exit within 20s of WM_CLOSE'
    }

    $lines = @((Get-RunLog $baseline) -split "`r?`n" | Where-Object { $_.Trim() })
    foreach ($line in $lines | Where-Object { $_ -match '\[(ERR|EROR|CRIT|FATAL)\]' }) {
        $failures += "log: $line"
    }

    if (-not ($lines | Where-Object { $_ -match 'Shutdown complete' })) {
        $failures += 'no "Shutdown complete" marker'
    }
}
finally {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-IsolatedDataRoot -Root $data.Root
}

Write-Host ''
Say 'working set (reported, not asserted):'
foreach ($sample in $samples) {
    Say ("  {0,-28} {1,4} MB  {2} handles" -f $sample.At, $sample.WorkingSetMb, $sample.Handles)
}

if ($failures.Count -eq 0) {
    Write-Host ''
    Say "PASS: $($modules.Count) modules x $Rounds rounds, each navigation log-confirmed; Settings is editable and offers indicator detection; clean shutdown."
    exit 0
}

Write-Host ''
Say '--- FAILED ---'
foreach ($failure in $failures) { Say "  $failure" }
exit 1
