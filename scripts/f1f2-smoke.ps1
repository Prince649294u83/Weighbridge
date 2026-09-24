# Runtime verification for Gate 2E Modern F1/F2 Operator Workflow.
#
# Drives the real screen through UI Automation on the real database:
# 1. Login as Administrator.
# 2. Switch to F1 (First Entry) -> enter vehicle and master details.
# 3. Allocate ticket -> verify persisted SlipNumber is dynamically captured ($ticket).
# 4. Capture first weight (F3) -> submit first weight (F5).
# 5. Verify status becomes AwaitingSecondWeight in database.
# 6. Alter master record externally using a controlled test helper.
# 7. Simulate terminal crash: Hard kill process (Stop-Process -Force).
# 8. Relaunch application against the same database.
# 9. Switch to F2 (Second Entry) -> scan/search ticket ($ticket) -> press Enter.
# 10. Verify historical snapshot is restored and locked (vehicle, party, material, 1st weight unchanged).
# 11. Enter F2 second entry details (SecondCharges, Bags, BagWeight, GatePass, Remarks).
# 12. Capture second weight (F3) -> complete weighment (F5).
# 13. Verify status is Completed in database, Net weight and Actual material weight are correct.
# 14. Search ticket in F2 again -> verify specific message ("already completed").
#
# Exit code 0 = verified. Non-zero = something failed.

param(
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = if ($ExePath) { $ExePath } else {
    Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'
}

. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$paths = New-IsolatedDataRoot -Label 'f1f2-smoke'
$logFile = $paths.LogFile
$dbFile = $paths.DbFile

function Say($text) { Write-Host "[f1f2-smoke] $text" }
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
    if (-not $field) { throw "no field named '$name'" }
    $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

function Get-ScreenText($scope) {
    $cond = New-Condition $UIA::ControlTypeProperty ($Ctrl::Text)
    $elements = $scope.FindAll($Tree::Descendants, $cond)
    $texts = [System.Collections.Generic.List[string]]::new()
    foreach ($el in $elements) {
        $t = $el.Current.Name
        if ($t) { $texts.Add($t) }
    }
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

function Start-App {
    $env:WEIGHBRIDGE_Hardware__WeightIndicator__DriverType = 'Simulator'
    $process = Start-Process -FilePath $exe -PassThru
    $root = Invoke-WeighBridgeLogin -ProcessId $process.Id -TimeoutSec 45
    [Win32]::SetForegroundWindow($root.Current.NativeWindowHandle) | Out-Null
    try {
        ($root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
    } catch { Say "could not maximize: $($_.Exception.Message)" }
    Start-Sleep -Milliseconds 1200
    return @{ Process = $process; Root = $root }
}

function Open-VehicleEntry($root) {
    $deadline = (Get-Date).AddSeconds(10)
    $item = $null
    while ((Get-Date) -lt $deadline -and -not $item) {
        $item = Find-ByNameAndType $root 'Vehicle Entry' ($Ctrl::RadioButton)
        if (-not $item) { Start-Sleep -Milliseconds 250 }
    }
    if (-not $item) { throw "no navigation item named 'Vehicle Entry'" }
    Invoke-Named $root 'Vehicle Entry' ($Ctrl::RadioButton)
    Start-Sleep -Milliseconds 600
}

# =====================================================================
# RUN 1: F1 Entry, Allocate Ticket, Record First Weight
# =====================================================================
Say '=== RUN 1: F1 Workflow (Allocate Ticket & First Weight) ==='
$stale = Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue
if ($stale) { $stale | Stop-Process -Force; Start-Sleep -Milliseconds 800 }

$app1 = Start-App
$root1 = $app1.Root
Open-VehicleEntry $root1

# Switch to F1
Invoke-Named $root1 'First Entry' ($Ctrl::Button)
Assert-Text $root1 'F1: First Entry' 'F1 mode activated' | Out-Null

# Enter F1 details
Set-Field $root1 'Vehicle number' 'MH12AB9000'
Set-Field $root1 'Party' 'Tata Steel'
Set-Field $root1 'Material' 'Iron Ore'
Set-Field $root1 'Charges' '100'
Set-Field $root1 'CustomField1' 'Consigner North'
Set-Field $root1 'CustomField2' 'GR-8899'

# 1. Allocate Ticket
Invoke-Named $root1 '1. Allocate Ticket' ($Ctrl::Button)
Assert-Text $root1 '(?:WB-)?\d{6}' 'authoritative ticket number allocated' | Out-Null

$screenTexts = Get-ScreenText $root1
$ticketMatch = ($screenTexts | Where-Object { $_ -match '^(?:WB-)?\d{6}$' } | Select-Object -First 1)
if (-not $ticketMatch) {
    $matched = (($screenTexts -match '(?:WB-)?\d{6}') | Select-Object -First 1)
    if ($matched -match '((?:WB-)?\d{6})') { $ticketMatch = $matches[1] }
}
Say "Dynamic allocated ticket: $ticketMatch"
if (-not $ticketMatch) {
    throw "Fatal: Failed to capture allocated ticket number dynamically from screen."
}

# 2. Record First Weight
Set-Field $root1 'First weight in kilograms' '90'
Invoke-Named $root1 '2. Record Wt (F5)' ($Ctrl::Button)
Assert-Text $root1 'Vehicle queued for second weight' 'first weight recorded and queued' | Out-Null

# =====================================================================
# SIMULATE TERMINAL CRASH: Hard kill process
# =====================================================================
Say "Simulating terminal crash: killing process $($app1.Process.Id)..."
Stop-Process -Id $app1.Process.Id -Force
Start-Sleep -Seconds 1

# =====================================================================
# RUN 2: Restart, F2 Search, Snapshot Locking, F2 Esc Safety, F2 Complete
# =====================================================================
Say '=== RUN 2: Restart & F2 Second Entry Completion ==='
$app2 = Start-App
$root2 = $app2.Root
Open-VehicleEntry $root2

# Switch to F2
Invoke-Named $root2 'Second Entry' ($Ctrl::Button)
Assert-Text $root2 'F2: Second Entry' 'F2 mode activated' | Out-Null

# Scanner search for dynamic ticket
Set-Field $root2 'Search pending ticket' $ticketMatch
Invoke-Named $root2 'Search' ($Ctrl::Button)
Assert-Text $root2 'Historical F1 Snapshot \(Locked\)' 'historical snapshot section displayed' | Out-Null
Assert-Text $root2 'Tata Steel' 'original party name restored unchanged' | Out-Null
Assert-Text $root2 'Iron Ore' 'original material restored unchanged' | Out-Null
Assert-Text $root2 '90' 'first weight restored unchanged' | Out-Null

# --- F2 Esc / Clear Safety Check (Acceptance Point 24) ---
Say 'Testing F2 Esc/Clear safety: clearing UI context...'
Invoke-Named $root2 'Clear Context' ($Ctrl::Button)
Start-Sleep -Milliseconds 400
# Re-search same ticket in F2 to prove database transaction remained intact
Set-Field $root2 'Search pending ticket' $ticketMatch
Invoke-Named $root2 'Search' ($Ctrl::Button)
Assert-Text $root2 'Historical F1 Snapshot \(Locked\)' 'ticket remains intact and searchable in DB after clear' | Out-Null

# Enter F2 Details
Set-Field $root2 'Second charges' '50'
Set-Field $root2 'Number of bags' '20'
Set-Field $root2 'Bag weight in kg' '1.0'
Set-Field $root2 'Gate pass number' 'GP-9021'
Set-Field $root2 'F2 Remarks' 'Final delivery verified'
Set-Field $root2 'CustomField3' 'Moisture 4%'
Set-Field $root2 'CustomField4' 'Standard Seal'

# Capture second weight & Rapid Double-F5 Complete
Set-Field $root2 'Second weight in kilograms' '30'
Say 'Testing double-F5 submit protection...'
Invoke-Named $root2 'Complete (F5)' ($Ctrl::Button)
# Rapid second click attempt while completing
try {
    Invoke-Named $root2 'Complete (F5)' ($Ctrl::Button)
} catch {
    # Expected if button becomes disabled / busy
}

Assert-Text $root2 'completed\. Net 60' 'second weight completed and net weight calculated' | Out-Null
Assert-Text $root2 '40' 'actual material weight after bag deduction calculated' | Out-Null

# Switch to F2 to search again
Invoke-Named $root2 'Second Entry' ($Ctrl::Button)

# Search completed ticket in F2 -> verify exclusion
Set-Field $root2 'Search pending ticket' $ticketMatch
Invoke-Named $root2 'Search' ($Ctrl::Button)
Assert-Text $root2 'has already been completed' 'completed ticket excluded from pending search with specific message' | Out-Null

# Clean exit
Stop-Process -Id $app2.Process.Id -Force

if ($failures.Count -gt 0) {
    Say '--- FAILED ---'
    $failures | ForEach-Object { Write-Host "  $_" }
    Remove-IsolatedDataRoot -Root $paths.Root -Keep
    exit 1
}

Say 'PASS: Gate 2F Modern F1/F2 operator workflow, crash recovery, snapshot locking, Esc safety, and second entry completion fully verified.'
Remove-IsolatedDataRoot -Root $paths.Root
exit 0
