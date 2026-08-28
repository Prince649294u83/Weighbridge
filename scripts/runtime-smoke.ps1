# Runtime smoke test for WeighBridge.Modern (Phase 0.5 verification).
# Launches the app, drives navigation + theme toggle via UIA, waits for a
# background health-refresh tick, then closes the window and verifies the
# shutdown marker in the log.
#
# Exit code 0 = verified. Non-zero = something failed (details printed).

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'

# Runs against its own data root. Launching the application against the live installation put
# this run's log lines, preferences and any records it created into the operator's data.
. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$paths = New-IsolatedDataRoot -Label 'runtime'
$logFile = $paths.LogFile

function Say($text) { Write-Host "[smoke] $text" }
Say "isolated data root: $($paths.Root)"

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe" }

# --- 1. Baseline log state ------------------------------------------------
$baseline = if (Test-Path $logFile) { (Get-Item $logFile).Length } else { 0 }
Say "Baseline log length: $baseline"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'login-helper.ps1')

# Any terminating error from here on must not leave a 180 MB WPF process running; a
# leaked instance holds the SQLite file and poisons the next run.
trap {
    if ($process -and -not $process.HasExited) {
        $process.Kill()
        Say "Killed leftover process $($process.Id)."
    }
    break
}

# --- 2. Launch and authenticate -------------------------------------------
# The default configuration points the indicator at COM1, which most machines do not
# have; a serial-open failure is environmental noise for a shell/navigation run, and
# whitelisting error lines in the log gate would weaken it. The simulator is selected
# explicitly instead, exactly as hardware-smoke.ps1 does.
$env:WEIGHBRIDGE_Hardware__WeightIndicator__DriverType = 'Simulator'
$process = Start-Process -FilePath $exe -PassThru
$processId = $process.Id
Say "Launched pid=$processId"

# Invoke-WeighBridgeLogin identifies the shell by AutomationId and throws on failure.
# The previous version took the process's first top-level window as the shell, which
# after Prompt 10 is the login dialog - so every lookup below silently found nothing.
$root = Invoke-WeighBridgeLogin -ProcessId $processId -TimeoutSec 45

$windowName = $root.Current.Name
$isResponsive = $root.Current.IsEnabled
Say "Main window up: '$windowName' (enabled=$isResponsive)"

# --- 3. Exercise the shell -------------------------------------------------
function Find-ByAutomationId($scope, $id) {
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)))
}

function Find-ByNameAndType($scope, $name, $type) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)),
        (New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $type)))
    return $scope.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Invoke-Named($scope, $name, $type) {
    $element = Find-ByNameAndType $scope $name $type
    if (-not $element) { throw "no $type named '$name'" }
    if (-not $element.Current.IsEnabled) { throw "the $type named '$name' is disabled" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 400
}

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32 {
    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); // LEFTDOWN
        System.Threading.Thread.Sleep(40);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); // LEFTUP
    }
}
'@

# A real mouse click, not SelectionItemPattern.Select(). Select() only sets IsChecked
# on the radio button; the navigation command is bound to Click, so Select() left the
# rail highlighted while the content never changed - a silent false pass.
function Invoke-RealClick($element) {
    $point = $element.GetClickablePoint()
    [Win32]::Click([int]$point.X, [int]$point.Y)
}

$failures = [System.Collections.Generic.List[string]]::new()

# Reads only what this run appended. Opened with ReadWrite sharing because the app
# still holds the file.
function Get-RunLog {
    $stream = [System.IO.File]::Open($logFile, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($baseline, 'Begin') | Out-Null
        return (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    } finally { $stream.Dispose() }
}

function Get-NavCount($viewModel) {
    return ([regex]::Matches((Get-RunLog), "Navigated to $viewModel")).Count
}

# Waits for the click's effect instead of sleeping a fixed guess. A count delta, not
# mere presence: the shell restores the last module at startup, so 'has a Navigated
# line' is already true for that one module before any click happens.
function Wait-ForNavigation($viewModel, $before, $timeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 150
        if ((Get-NavCount $viewModel) -gt $before) { return $true }
    }
    return $false
}

$navTargets = [ordered]@{
    'Dashboard'      = 'DashboardViewModel'
    'Vehicle Entry'  = 'VehicleEntryViewModel'
    'Duplicate Slip' = 'DuplicateSlipViewModel'
    'Reports'        = 'ReportsViewModel'
    'Masters'        = 'MastersViewModel'
    'Settings'       = 'SettingsViewModel'
    'Administration' = 'AdministrationViewModel'
}
$navigated = @()

# Synthesized mouse input goes to whatever window has focus, so the window is raised
# once before the first click. Without this the first click was landing nowhere.
[Win32]::SetForegroundWindow($root.Current.NativeWindowHandle) | Out-Null
try {
    ($root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
} catch { }
Start-Sleep -Milliseconds 1500

foreach ($target in $navTargets.Keys) {
    $viewModel = $navTargets[$target]
    try {
        $deadline = (Get-Date).AddSeconds(10)
        $item = $null
        while ((Get-Date) -lt $deadline -and -not $item) {
            $item = Find-ByNameAndType $root $target ([System.Windows.Automation.ControlType]::RadioButton)
            if (-not $item) { Start-Sleep -Milliseconds 250 }
        }
        if (-not $item) {
            $failures.Add("navigation: no radio button named '$target'")
            continue
        }

        # One retry: a synthesized click is occasionally eaten by the compositor, and a
        # retry distinguishes lost input from a navigation that genuinely never fires.
        $confirmed = $false
        foreach ($attempt in 1, 2) {
            $before = Get-NavCount $viewModel
            try {
                Invoke-Named $root $target ([System.Windows.Automation.ControlType]::RadioButton)
            } catch {
                Invoke-RealClick $item
            }
            if (Wait-ForNavigation $viewModel $before 4000) { $confirmed = $true; break }
            Say "retrying click on '$target' (attempt $attempt produced no navigation)"
        }

        if ($confirmed) {
            $navigated += $target
        } else {
            $failures.Add("navigation: clicked '$target' twice, no new 'Navigated to $viewModel' in the log")
        }
    } catch {
        $failures.Add("navigation: '$target' -> $($_.Exception.Message)")
    }
}
Say ("Navigation clicked: " + ($navigated -join ' > '))

# Theme toggle (UIA button, tooltip text has it)
try {
    $themeBtn = Find-ByNameAndType $root 'Switch between the light and dark theme' ([System.Windows.Automation.ControlType]::Button)
    if ($themeBtn) {
        $invoke = $themeBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $invoke.Invoke()
        Start-Sleep -Milliseconds 600
        $invoke.Invoke()
        Start-Sleep -Milliseconds 600
        Say 'Theme toggled twice (off, on)'
    } else {
        $failures.Add('theme: toggle button not found')
    }
} catch {
    $failures.Add("theme: $($_.Exception.Message)")
}

# --- 3b. Dialogs: drive the real dialog framework through the shell -------
# The shell's placeholder modules raise no dialogs on their own, so the
# framework is exercised where it is reachable: the status bar refresh runs a
# real command through the pipeline, and a navigation failure is the one path
# that shows a dialog. Anything not reachable from the shell is left to the
# unit tests rather than faked here.
try {
    $refresh = Find-ByNameAndType $root 'Re-check every subsystem now' ([System.Windows.Automation.ControlType]::Button)
    if ($refresh) {
        $refresh.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Seconds 2
        Say 'Status refresh command invoked'
    } else {
        $failures.Add('status refresh: button not found')
    }
} catch {
    $failures.Add("status refresh: $($_.Exception.Message)")
}

# --- 3c. Screenshot, so glyph rendering is looked at rather than assumed ---
try {
    Add-Type -AssemblyName System.Drawing
    $r = $root.Current.BoundingRectangle
    $bitmap = New-Object System.Drawing.Bitmap([int]$r.Width), ([int]$r.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bitmap.Size)
    $shot = Join-Path $env:TEMP 'weighbridge-smoke.png'
    $bitmap.Save($shot, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    Say "Screenshot: $shot"
} catch {
    Say "Screenshot skipped: $($_.Exception.Message)"
}

# --- 4. Wait for one background subsystem tick (30s interval) --------------
Say 'Waiting 35s for the background health/status tickâ€¦'
Start-Sleep -Seconds 35
$process.Refresh()
if ($process.HasExited) { Write-Error "App exited unexpectedly during the wait (code $($process.ExitCode))" }

# --- 5. Graceful close ------------------------------------------------------
try {
    $closeBtn = Find-ByNameAndType $root 'Close' ([System.Windows.Automation.ControlType]::Button)
    if ($closeBtn) {
        $closeBtn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Say 'Close button invoked'
    } else {
        $failures.Add('close: button not found, fell back to WM_CLOSE')
        [Win32]::PostMessage($root.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    }
} catch {
    $failures.Add("close: $($_.Exception.Message), fell back to WM_CLOSE")
    [Win32]::PostMessage($root.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
}

$waitDeadline = (Get-Date).AddSeconds(20)
$cleanExit = $false
while ((Get-Date) -lt $waitDeadline) {
    Start-Sleep -Milliseconds 500
    $process.Refresh()
    if ($process.HasExited) { $cleanExit = $true; break }
}
if (-not $cleanExit) {
    Stop-Process -Id $processId -Force
    Write-Error 'App did not exit within 20s of the close request - killed'
}
Say "Process exited cleanly (code $($process.ExitCode))"

# --- 6. Inspect the log -----------------------------------------------------
# Only what this run appended, so a previous run's lines cannot vouch for this one.
$runLog = (Get-RunLog) -split "`r?`n"
Say "This run appended $($runLog.Count) log lines"

$errors = $runLog | Where-Object { $_ -match '\[ERR\]|\[CRI\]|Unhandled|Unobserved' } | Select-Object -First 20
if ($errors) {
    Say '--- ERROR-LEVEL LOG ENTRIES ---'
    $errors | ForEach-Object { Write-Host $_ }
    $failures.Add('log contains error-level entries')
}

# Navigation is already verified inline, by a count delta per click. Re-checking
# presence here would be weaker: the shell restores one module at startup, so its
# 'Navigated to' line exists before any click.

$themeApplied = @($runLog | Where-Object { $_ -match 'Applied theme' })
if ($themeApplied.Count -lt 3) {
    $failures.Add("theme: expected the startup apply plus 2 toggles, log shows $($themeApplied.Count)")
}

if (-not ($runLog | Where-Object { $_ -match 'Subsystem monitoring started' })) {
    $failures.Add('background: no "Subsystem monitoring started" line')
}
if (-not ($runLog | Where-Object { $_ -match 'Subsystem monitoring stopped' })) {
    $failures.Add('shutdown: monitoring was never stopped')
}
if (-not ($runLog | Where-Object { $_ -match 'Background task manager started' })) {
    $failures.Add('background: task manager never started')
}

$shutdown = $runLog | Where-Object { $_ -match 'Shutdown complete' } | Select-Object -Last 1
if ($shutdown) {
    Say ("Shutdown marker: " + $shutdown.Trim())
} else {
    $failures.Add('shutdown marker "==== Shutdown complete ====" missing from the log')
}

if ($failures.Count -gt 0) {
    Say '--- FAILED ---'
    $failures | ForEach-Object { Write-Host "  $_" }
    # Kept: the log of a failed run is what a diagnosis starts from.
    Remove-IsolatedDataRoot -Root $paths.Root -Keep
    exit 1
}

Say 'PASS: startup, all 7 modules navigated (log-confirmed), theme toggle, status refresh, background tick, graceful shutdown, no error-level entries.'
Remove-IsolatedDataRoot -Root $paths.Root
exit 0
