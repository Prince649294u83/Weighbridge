# Runtime verification for the Vehicle Entry module (Prompt 7, Phase K).
#
# Drives the real screen through UI Automation on the real database: opens a
# weighment, records both weights, rejects two invalid operations, cancels one
# through the confirmation dialog, then RESTARTS the application and checks the
# work is still there. Nothing is stubbed and nothing is read from memory - every
# assertion is made against what the window actually shows.
#
# RUN 1 starts from a DELETED database, so first launch on a new machine is what
# is being tested: the schema has to be migrated before the first module reads
# from it. This is a development smoke test and it writes test weighments - it is
# not for a machine holding real weighbridge data.
#
# Exit code 0 = verified. Non-zero = something failed (details printed).

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'

# Runs against its own data root, not the installation's. This script needs an empty database
# to exercise the migration path, and it used to get one by deleting the database at the real
# path â€” which destroyed the operator's weighment history on any machine with live data.
. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$paths = New-IsolatedDataRoot -Label 'vehicle-entry'
$logFile = $paths.LogFile
$dbFile = $paths.DbFile

function Say($text) { Write-Host "[vehicle-entry] $text" }
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

# Buttons are invoked through the pattern rather than by a synthesized click: the
# form is taller than the window, so the lower buttons have no clickable point, and
# Invoke() runs the same bound command a click would. The navigation rail still gets
# a real click - there, Select() sets IsChecked without navigating.
function Invoke-Named($scope, $name, $type) {
    $element = Find-ByNameAndType $scope $name $type
    if (-not $element) { throw "no $type named '$name'" }
    if (-not $element.Current.IsEnabled) { throw "the $type named '$name' is disabled" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
}

# Every editable field binds with UpdateSourceTrigger=PropertyChanged, so setting
# the value through the pattern reaches the ViewModel the same way typing does.
function Set-Field($scope, $name, $value) {
    $field = Find-ByNameAndType $scope $name ($Ctrl::Edit)
    if (-not $field) {
        $field = Find-ByNameAndType $scope $name ($Ctrl::ComboBox)
    }
    if (-not $field) { throw "no text box named '$name'" }
    $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}

# A picker's closed box draws its text through a template, and templated text is outside
# the automation control view, so it cannot be read with Get-ScreenText. The selected item
# is read through the selection pattern instead - a non-editable ComboBox offers no value
# pattern, and the item and the closed box are rendered from the same object.
function Get-PickerText($scope, $name) {
    $picker = Find-ByNameAndType $scope $name ($Ctrl::ComboBox)
    if (-not $picker) { throw "no picker named '$name'" }
    $selected = ($picker.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)).Current.GetSelection()
    if ($selected.Count -eq 0) { return '' }
    return $selected[0].Current.Name
}

# Text on screen, read back from the automation tree. WPF TextBlocks surface as
# Text elements whose Name is what they display, so this asserts what an operator
# would actually see rather than what a property holds.
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

function Assert-NoText($scope, $pattern, $what) {
    Start-Sleep -Milliseconds 800
    if ((Get-ScreenText $scope) -match $pattern) {
        $failures.Add("$what - '$pattern' is on screen and should not be")
        Say "  FAIL: $what"
        return $false
    }
    Say "  ok: $what"
    return $true
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

    # Maximized, so the whole screen is on screen: a field that is scrolled out of
    # view has no clickable point and cannot be read back either. The call is on one
    # line because PowerShell cannot start a line with a method on the previous value.
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
    foreach ($attempt in 1, 2) {
        $before = ([regex]::Matches((Get-RunLog), 'Navigated to VehicleEntryViewModel')).Count
        try {
            Invoke-Named $root 'Vehicle Entry' ($Ctrl::RadioButton)
        } catch {
            Invoke-RealClick $item
        }
        $navDeadline = (Get-Date).AddSeconds(6)
        while ((Get-Date) -lt $navDeadline) {
            Start-Sleep -Milliseconds 200
            if (([regex]::Matches((Get-RunLog), 'Navigated to VehicleEntryViewModel')).Count -gt $before) { return }
        }
        Say "retrying the click on 'Vehicle Entry' (attempt $attempt produced no navigation)"
    }
    throw 'clicked Vehicle Entry twice, the log shows no navigation'
}

function Save-Shot($root, $name) {
    try {
        $r = $root.Current.BoundingRectangle
        $bitmap = New-Object System.Drawing.Bitmap([int]$r.Width), ([int]$r.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bitmap.Size)
        $shot = Join-Path $env:TEMP "weighbridge-$name.png"
        $bitmap.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png)
        $graphics.Dispose(); $bitmap.Dispose()
        Say "screenshot: $shot"
    } catch { Say "screenshot failed: $($_.Exception.Message)" }
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
    Stop-Process -Id $app.Process.Id -Force
    $failures.Add('app did not exit within 20s of the close request - killed')
}

# The dialog is an owned chromeless window, so it is matched on its own title and control
# type rather than by elimination: the button that opens it, the button that confirms it
# and the window itself all carry the same wording, and the process also owns windows that
# are not dialogs at all.
function Find-Dialog($processId, $title, $timeoutMs = 6000) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Condition $UIA::ProcessIdProperty $processId),
        (New-Condition $UIA::NameProperty $title),
        (New-Condition $UIA::ControlTypeProperty ($Ctrl::Window)))
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 250
        $dialog = $UIA::RootElement.FindFirst($Tree::Descendants, $cond)
        if ($dialog) { return $dialog }
    }
    return $null
}

# =====================================================================
# RUN 1 - first launch on a machine with no database, then do the work
# =====================================================================
Say '=== RUN 1: first launch, weighing, rejecting, cancelling ==='

# A previous run that failed part way through leaves its window open, and the database
# cannot be replaced underneath it.
$stale = Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue
if ($stale) {
    Say "closing $($stale.Count) instance(s) left over from an earlier run"
    $stale | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# The migration has to run before the first module reads a table. With a schema already in
# place this whole path goes untested, and it is the path every new installation takes. The
# isolated root supplies that empty state; this used to delete the real database to get it.
if (Test-Path $dbFile) {
    Write-Error "database already exists in the isolated root: $dbFile - isolation is not in effect"
}
$dbBefore = 0
Say "database file before: $dbBefore bytes"

$app = Start-App
Say "main window up: '$($app.Root.Current.Name)'"
$root = $app.Root
$pid1 = $app.Process.Id

Open-VehicleEntry $root
Say 'navigated to Vehicle Entry'

# --- 0. What the operator sees before touching anything ---------------
# The picker is bound to option objects; showing the object instead of its label tells
# the operator nothing about how the vehicle arrived.
$arrival = Get-PickerText $root 'Whether the vehicle arrived loaded or empty'
Say "arrival picker reads: '$arrival'"
if ($arrival -notmatch '^Arrived loaded') {
    $failures.Add("the arrival picker shows '$arrival', not the option's wording")
    Say '  FAIL: the arrival picker does not read as words'
} else {
    Say '  ok: the arrival picker reads as words'
}

# Nothing has been weighed on a new database, and an empty grid on its own does not say
# whether that means idle or broken.
Assert-Text $root 'Nothing waiting' 'the empty waiting list explains itself' | Out-Null
Assert-Text $root 'No weighment in hand' 'the empty current-weighment card explains itself' | Out-Null

# --- 1. Open a weighment ---------------------------------------------
Set-Field $root 'Vehicle number' 'MH12AB1234'
Set-Field $root 'Party' 'Acme Cement'
Set-Field $root 'Material' 'Clinker'
Set-Field $root 'Driver' 'R. Singh'
Invoke-Named $root 'Open weighment' ($Ctrl::Button)

$slip = $null
if (Assert-Text $root 'WB-\d{6}' 'a slip number was allocated and shown') {
    $slip = (((Get-ScreenText $root) -match 'WB-\d{6}') | Select-Object -First 1)
    Say "  slip: $slip"
}
Assert-Text $root 'First weight pending' 'the stage reads as first-weight-pending' | Out-Null
Assert-Text $root 'MH12AB1234' 'the vehicle is shown on the current weighment' | Out-Null
Assert-Text $root 'Record gross weight' 'the next action tells the operator what to do' | Out-Null

# --- 2. Invalid: record with no weight typed --------------------------
Say 'invalid operation 1: recording with an empty weight box'
Invoke-Named $root 'Record first weight' ($Ctrl::Button)
Assert-Text $root 'Enter the weight in kilograms' 'the refusal is reported on screen' | Out-Null
Assert-Text $root 'First weight pending' 'the stage did not advance' | Out-Null

# --- 3. First weight --------------------------------------------------
Set-Field $root 'Weight in kilograms' '32500'
Invoke-Named $root 'Record first weight' ($Ctrl::Button)
Assert-Text $root 'Awaiting second weight' 'the first weight advanced the stage' | Out-Null
Assert-Text $root '32,500' 'the gross weight is shown' | Out-Null
Assert-Text $root 'Record tare weight' 'the next action changed' | Out-Null

# --- 4. Invalid: a tare heavier than the gross ------------------------
Say 'invalid operation 2: a tare heavier than the gross'
Set-Field $root 'Weight in kilograms' '40000'
Invoke-Named $root 'Record second weight' ($Ctrl::Button)
Assert-Text $root 'must be greater than' 'the impossible net was refused with a reason' | Out-Null
Assert-Text $root 'Awaiting second weight' 'the weighment is still workable' | Out-Null
Save-Shot $root 'vehicle-entry-refusal'

# --- 5. Second weight completes it ------------------------------------
Set-Field $root 'Weight in kilograms' '12250.5'
Invoke-Named $root 'Record second weight' ($Ctrl::Button)
Assert-Text $root '^Completed$' 'the stage badge reads Completed' | Out-Null
Assert-Text $root '20,249.5' 'the net is gross minus tare, to the half kilo' | Out-Null

# Nothing is asked of the operator for a closed weighment. Printing the slip is a later
# module, so the screen must not leave the previous instruction standing either.
Assert-NoText $root 'Record tare weight' 'the finished weighment asks for nothing more' | Out-Null
Save-Shot $root 'vehicle-entry-completed'

# --- 6. A second weighment, left waiting for the restart check --------
Set-Field $root 'Vehicle number' 'MH14CD5678'
Set-Field $root 'Party' 'Northern Aggregates'
Invoke-Named $root 'Open weighment' ($Ctrl::Button)
Set-Field $root 'Weight in kilograms' '28000'
Invoke-Named $root 'Record first weight' ($Ctrl::Button)
Assert-Text $root 'Awaiting second weight' 'the second vehicle is waiting for its tare' | Out-Null
Assert-Text $root 'MH14CD5678' 'the waiting vehicle is listed' | Out-Null

# --- 7. A third, cancelled through the confirmation dialog ------------
Set-Field $root 'Vehicle number' 'MH99ZZ0001'
Invoke-Named $root 'Open weighment' ($Ctrl::Button)
Set-Field $root 'Reason for cancelling' 'Wrong vehicle on the platform'
Invoke-Named $root 'Cancel weighment' ($Ctrl::Button)

$dialog = Find-Dialog $pid1 'Cancel weighment'
if (-not $dialog) {
    $failures.Add('cancel: the confirmation dialog never appeared')
    Say '  FAIL: confirmation dialog did not appear'
} else {
    Say '  ok: the confirmation dialog appeared'
    Save-Shot $dialog 'vehicle-entry-confirm-dialog'
    Assert-Text $dialog 'kept for the audit' 'the dialog says what cancelling does' | Out-Null

    # Decline first: a confirmation that ignores "no" is not a confirmation.
    try {
        Invoke-Named $dialog 'Keep it' ($Ctrl::Button)
        Start-Sleep -Milliseconds 800
        Assert-Text $root 'First weight pending' 'declining left the weighment alone' | Out-Null
    } catch {
        $failures.Add("cancel: could not decline the dialog - $($_.Exception.Message)")
    }

    # Then accept.
    Invoke-Named $root 'Cancel weighment' ($Ctrl::Button)
    $dialog = Find-Dialog $pid1 'Cancel weighment'
    if (-not $dialog) {
        $failures.Add('cancel: the confirmation dialog did not reappear')
    } else {
        Invoke-Named $dialog 'Cancel weighment' ($Ctrl::Button)
        Start-Sleep -Seconds 1
        Assert-Text $root 'WB-\d{6} cancelled' 'the cancellation was confirmed on screen' | Out-Null
    }
}

Save-Shot $root 'vehicle-entry-after-cancel'
Stop-App $app

$run1Log = (Get-RunLog) -split "`r?`n"
if (-not ($run1Log | Where-Object { $_ -match 'Shutdown complete' })) {
    $failures.Add('run 1: shutdown marker missing')
}
$errors1 = $run1Log | Where-Object { $_ -match '\[ERR\]|\[CRI\]|Unhandled|Unobserved' } | Select-Object -First 10
if ($errors1) {
    Say '--- ERROR-LEVEL ENTRIES (run 1) ---'
    $errors1 | ForEach-Object { Write-Host "  $_" }
    $failures.Add('run 1: log contains error-level entries')
}

$dbAfter = (Get-Item $dbFile).Length
Say "database file after: $dbAfter bytes (was $dbBefore)"
if ($dbAfter -le $dbBefore) {
    $failures.Add("persistence: the database file did not grow ($dbBefore -> $dbAfter)")
}

# =====================================================================
# RUN 2 - the application is restarted; the work has to still be there
# =====================================================================
Say '=== RUN 2: restart, and check the work survived ==='
$app2 = Start-App
$root2 = $app2.Root
Open-VehicleEntry $root2
Say 'navigated to Vehicle Entry on the restarted application'

Assert-Text $root2 'MH14CD5678' 'the waiting vehicle survived the restart' | Out-Null
Assert-Text $root2 '28,000' 'its recorded weight survived the restart' | Out-Null
Assert-NoText $root2 'MH99ZZ0001' 'the cancelled weighment is not waiting to be finished' | Out-Null
Save-Shot $root2 'vehicle-entry-after-restart'

Stop-App $app2

# =====================================================================
$runLog = (Get-RunLog) -split "`r?`n"
Say "both runs appended $($runLog.Count) log lines"

$errors = $runLog | Where-Object { $_ -match '\[ERR\]|\[CRI\]|Unhandled|Unobserved' } | Select-Object -First 20
if ($errors -and -not ($failures -contains 'run 1: log contains error-level entries')) {
    Say '--- ERROR-LEVEL ENTRIES ---'
    $errors | ForEach-Object { Write-Host "  $_" }
    $failures.Add('log contains error-level entries')
}

$shutdowns = @($runLog | Where-Object { $_ -match 'Shutdown complete' })
if ($shutdowns.Count -lt 2) {
    $failures.Add("shutdown: expected 2 clean shutdowns, log shows $($shutdowns.Count)")
}

if ($failures.Count -gt 0) {
    Say '--- FAILED ---'
    $failures | ForEach-Object { Write-Host "  $_" }
    # Kept: the database and log of a failed run are what a diagnosis starts from.
    Remove-IsolatedDataRoot -Root $paths.Root -Keep
    exit 1
}

Say 'PASS: slip allocated, both weights recorded, net correct, two invalid operations refused with reasons, cancellation confirmed through the dialog, work survived a restart, two clean shutdowns, no error-level entries.'
Remove-IsolatedDataRoot -Root $paths.Root
exit 0
