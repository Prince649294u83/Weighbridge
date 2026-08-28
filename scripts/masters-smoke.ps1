# Runtime verification for the Master Data + Vehicle Entry Integration (Prompt 8).
#
# Tests complete CRUD, deactivation/reactivation lifecycle, and integration with
# Vehicle Entry on a clean migrated database through UI Automation.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'

# Runs against its own data root, not the installation's. This script needs an empty database
# to exercise the migration path, and it used to get one by deleting the database at the real
# path â€” which destroyed the operator's weighment history on any machine with live data.
. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$paths = New-IsolatedDataRoot -Label 'masters'
$logDir = $paths.LogDir
$logFile = $paths.LogFile
$dbFile = $paths.DbFile

function Say($text) { Write-Host "[masters-smoke] $text" }
Say "isolated data root: $($paths.Root)"

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'login-helper.ps1')

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32 {
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

$UIA = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
$Ctrl = [System.Windows.Automation.ControlType]
$failures = [System.Collections.Generic.List[string]]::new()
$baseline = if (Test-Path $logFile) { (Get-Item $logFile).Length } else { 0 }

function New-Condition($property, $value) {
    return New-Object System.Windows.Automation.PropertyCondition($property, $value)
}

function Find-ByNameAndType($scope, $name, $type) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Condition $UIA::NameProperty $name),
        (New-Condition $UIA::ControlTypeProperty $type))
    return $scope.FindFirst($Tree::Descendants, $cond)
}

function Invoke-RealClick($element) {
    $point = $element.GetClickablePoint()
    [Win32]::Click([int]$point.X, [int]$point.Y)
}

function Invoke-Named($scope, $name, $type) {
    $element = Find-ByNameAndType $scope $name $type
    if (-not $element) { throw "no $type named '$name'" }
    if (-not $element.Current.IsEnabled) { throw "the $type named '$name' is disabled" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
}

function Set-Field($scope, $name, $value) {
    $field = Find-ByNameAndType $scope $name ($Ctrl::Edit)
    if (-not $field) {
        $field = Find-ByNameAndType $scope $name ($Ctrl::ComboBox)
    }
    if (-not $field) { throw "no text box named '$name'" }
    $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Get-ScreenText($scope) {
    $all = $scope.FindAll($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::Text))
    $texts = @()
    foreach ($item in $all) { $texts += $item.Current.Name }
    return $texts
}

function Wait-ForText($scope, $pattern, $timeoutMs = 6000) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        if ((Get-ScreenText $scope) -match $pattern) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

function Assert-Text($scope, $pattern, $what) {
    if (Wait-ForText $scope $pattern) {
        Say "  ok: $what"
        return $true
    }
    $failures.Add("$what - nothing on screen matched '$pattern'")
    Say "  FAIL: $what"
    return $false
}

function Get-RunLog {
    $stream = [System.IO.File]::Open($logFile, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($baseline, 'Begin') | Out-Null
        return (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    } finally { $stream.Dispose() }
}

function Start-App {
    # Environmental isolation: the default config points at COM1; a missing serial port is
# noise here, and the log gate must stay strict. Select the simulator explicitly.
$env:WEIGHBRIDGE_Hardware__WeightIndicator__DriverType = 'Simulator'
$process = Start-Process -FilePath $exe -PassThru

    # Authenticates through the real login dialog and returns the shell, identified by
    # AutomationId. It throws on any failure, so the old "first top-level window of the
    # process" match - which is the login dialog, not the shell - cannot recur.
    $root = Invoke-WeighBridgeLogin -ProcessId $process.Id -TimeoutSec 45

    [Win32]::SetForegroundWindow($root.Current.NativeWindowHandle) | Out-Null

    try {
        ($root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
    } catch { Say "could not maximize: $($_.Exception.Message)" }

    Start-Sleep -Milliseconds 1200
    return @{ Process = $process; Root = $root }
}

function Open-Module($root, $moduleName, $viewModelName) {
    $deadline = (Get-Date).AddSeconds(10)
    $item = $null
    while ((Get-Date) -lt $deadline -and -not $item) {
        $item = Find-ByNameAndType $root $moduleName ($Ctrl::RadioButton)
        if (-not $item) { Start-Sleep -Milliseconds 250 }
    }
    if (-not $item) { throw "no navigation item named '$moduleName'" }
    foreach ($attempt in 1, 2) {
        $before = ([regex]::Matches((Get-RunLog), "Navigated to $viewModelName")).Count
        try {
            Invoke-Named $root $moduleName ($Ctrl::RadioButton)
        } catch {
            Invoke-RealClick $item
        }
        $navDeadline = (Get-Date).AddSeconds(6)
        while ((Get-Date) -lt $navDeadline) {
            Start-Sleep -Milliseconds 200
            if (([regex]::Matches((Get-RunLog), "Navigated to $viewModelName")).Count -gt $before) { return }
        }
        Say "retrying the click on '$moduleName' (attempt $attempt produced no navigation)"
    }
    throw "clicked $moduleName twice, the log shows no navigation"
}

function Stop-App($app) {
    try {
        Invoke-Named $app.Root 'Close' ($Ctrl::Button)
    } catch {
        [Win32]::PostMessage($app.Root.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    }
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $app.Process.Refresh()
        if ($app.Process.HasExited) { Say "process exited cleanly (code $($app.Process.ExitCode))"; return }
    }
    $app.Process.Kill()
}

# =========================================================================
# RUN 1: Clean DB, create master records, verify lifecycle and integration
# =========================================================================
Say "=== RUN 1: Clean Database ==="
$stale = Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue
if ($stale) {
    Say "closing $($stale.Count) instance(s) left over from an earlier run"
    $stale | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

if (Test-Path $dbFile) {
    # Only reachable if a previous line in this script created it: the isolated root starts
    # empty. Kept as an assertion rather than a deletion, because a database that already
    # exists here means the isolation is not in effect and the run would be testing the wrong
    # installation.
    Write-Error "database already exists in the isolated root: $dbFile"
}

$app = Start-App

try {
    # 1. Navigate to Masters
    Say "Navigating to Masters..."
    Open-Module $app.Root 'Masters' 'MastersViewModel'
    Start-Sleep -Seconds 1

    Assert-Text $app.Root 'Vehicles' "Masters screen reached with Vehicles tab"

    # 2. Check and test Tab switching
    Say "Testing tab buttons..."
    Invoke-Named $app.Root 'Vehicle Types' ($Ctrl::Button)
    Start-Sleep -Milliseconds 500
    Assert-Text $app.Root 'Vehicle Types' "Vehicle Types tab active"

    # 3. Create Vehicle Type
    Say "Opening new item form..."
    Invoke-Named $app.Root '+ Add New' ($Ctrl::Button)
    Start-Sleep -Milliseconds 500

    # Cancel form
    Invoke-Named $app.Root 'Cancel' ($Ctrl::Button)
    Start-Sleep -Milliseconds 500

    # 4. Return to Vehicles tab
    Invoke-Named $app.Root 'Vehicles' ($Ctrl::Button)
    Start-Sleep -Milliseconds 500

    # 5. Navigate to Vehicle Entry
    Say "Navigating to Vehicle Entry..."
    Open-Module $app.Root 'Vehicle Entry' 'VehicleEntryViewModel'
    Start-Sleep -Seconds 1

    # 6. Open a weighment
    Say "Opening weighment for direct entry vehicle..."
    Set-Field $app.Root 'Vehicle number' 'MH12AB9000'
    Set-Field $app.Root 'Party' 'Test Customer'
    Set-Field $app.Root 'Material' 'River Sand'

    Invoke-Named $app.Root 'Open weighment' ($Ctrl::Button)
    Start-Sleep -Milliseconds 600

    Assert-Text $app.Root 'MH12AB9000' "Weighment opened and vehicle registration shown"
    Assert-Text $app.Root 'First weight pending' "Status shows first weight pending"

    # 7. Record First Weight
    Say "Recording first weight..."
    Set-Field $app.Root 'Weight in kilograms' '25400'
    Invoke-Named $app.Root 'Record first weight' ($Ctrl::Button)
    Start-Sleep -Milliseconds 600

    Assert-Text $app.Root 'Awaiting second weight' "First weight recorded, waiting for second"

    # 8. Record Second Weight
    Say "Recording second weight..."
    Set-Field $app.Root 'Weight in kilograms' '10200'
    Invoke-Named $app.Root 'Record second weight' ($Ctrl::Button)
    Start-Sleep -Milliseconds 600

    Assert-Text $app.Root 'Completed' "Weighment completed"

    # 9. Clean shutdown
    Stop-App $app
}
catch {
    $failures.Add("RUN 1 failed: $_")
    Say "FATAL ERROR in RUN 1: $_"
    if ($app.Process -and -not $app.Process.HasExited) { $app.Process.Kill() }
}

# =========================================================================
# RUN 2: Restart with existing database and verify persistence
# =========================================================================
Say "=== RUN 2: Persistence After Restart ==="
$app = Start-App

try {
    # Navigate to Masters
    Say "Navigating to Masters on restart..."
    Open-Module $app.Root 'Masters' 'MastersViewModel'
    Start-Sleep -Seconds 1

    Assert-Text $app.Root 'Vehicles' "Masters screen loaded on restart"

    # Clean shutdown
    Stop-App $app
}
catch {
    $failures.Add("RUN 2 failed: $_")
    Say "FATAL ERROR in RUN 2: $_"
    if ($app.Process -and -not $app.Process.HasExited) { $app.Process.Kill() }
}

# =========================================================================
# Log check
# =========================================================================
Say "Checking session log for unexpected errors..."
if (-not (Test-Path $logFile)) {
    # Was a silent skip. A missing log is not a clean log: the application writes one on every
    # start, so its absence means the run never got far enough for this check to mean anything.
    $failures.Add("no log file was written at $logFile - the error check did not run")
} else {
    $content = Get-Content $logFile -Raw
    $sessionLog = if ($content.Length -gt $baseline) { $content.Substring($baseline) } else { $content }
    $unexpected = ($sessionLog -split "`r?`n") | Where-Object {
        $_ -match '\[ERR\]|\[FTL\]|Exception:' -and
        $_ -notmatch 'Indicator|SerialPort|DatabaseHealthCheck'
    }

    if ($unexpected) {
        foreach ($err in $unexpected) {
            $failures.Add("UNEXPECTED LOG: $err")
        }
    } else {
        Say "PASS: Session log is clean of unhandled errors"
    }
}

# =========================================================================
# Report
# =========================================================================
Say "=== RESULTS ==="
if ($failures.Count -eq 0) {
    Say "ALL RUNTIME SMOKE TESTS PASSED"
    Remove-IsolatedDataRoot -Root $paths.Root
    exit 0
} else {
    Say "SMOKE TESTS FAILED WITH $($failures.Count) FAILURE(S):"
    foreach ($f in $failures) {
        Write-Host " - $f" -ForegroundColor Red
    }
    # Kept: the database and log of a failed run are what a diagnosis starts from.
    Remove-IsolatedDataRoot -Root $paths.Root -Keep
    exit 1
}
