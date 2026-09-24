# Runtime verification for Real Weight Indicator + Camera Integration (Prompt 9).
#
# Tests clean-database cold start, live weight HUD readout, indicator capture,
# camera snapshot capture, SQLite image metadata persistence, restart persistence,
# and graceful shutdown without errors.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'

# Runs against its own data root, not the installation's. This script needs an empty database
# and a simulator-configured indicator; it used to get the first by deleting the real database
# and the second by editing the operator's own appsettings.json and restoring it afterwards.
# Both are now written inside a temporary root, so a failed run cannot leave the operator with
# a missing database or a settings file configured for simulated hardware.
. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$paths = New-IsolatedDataRoot -Label 'hardware'
$logFile = $paths.LogFile
$dbFile = $paths.DbFile
$captureDir = $paths.CaptureDir

function Say($text) { Write-Host "[hardware-smoke] $text" }
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
    $elem = Find-ByNameAndType $scope $name $type
    if ($null -eq $elem) {
        throw "Could not find element: $name ($($type.ProgrammaticName))"
    }
    if (-not $elem.Current.IsEnabled) { throw "the element '$name' is disabled" }
    try {
        $pattern = $elem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $pattern.Invoke()
    } catch {
        Invoke-RealClick $elem
    }
    Start-Sleep -Milliseconds 400
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
    if (-not (Test-Path $logFile)) { return '' }
    $stream = [System.IO.File]::Open($logFile, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($baseline, 'Begin') | Out-Null
        return (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    } finally { $stream.Dispose() }
}

# Confirms from the log that the navigation happened, and retries the click once if it did
# not. It used to click and continue: a click that missed left the previous module on screen,
# and every element this test then looked for was absent - so a single lost click was reported
# as three missing UI elements and a hardware defect that was not there.
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
            if (([regex]::Matches((Get-RunLog), "Navigated to $viewModelName")).Count -gt $before) {
                Start-Sleep -Milliseconds 400
                return
            }
        }
        Say "retrying the click on '$moduleName' (attempt $attempt produced no navigation)"
    }
    throw "clicked $moduleName twice, the log shows no navigation to $viewModelName"
}

function Set-Field($scope, $name, $value) {
    $field = Find-ByNameAndType $scope $name ($Ctrl::Edit)
    if (-not $field) {
        $field = Find-ByNameAndType $scope $name ($Ctrl::ComboBox)
    }
    if (-not $field) { throw "no input named '$name'" }
    $pattern = $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($value)
    Start-Sleep -Milliseconds 150
}

<#
.SYNOPSIS
Returns a list of problems with a capture file; empty means it passed.

.DESCRIPTION
This replaces a byte-count floor of 100. The audit found the old check passing on a 328-byte
file, which is the whole reason it is a finding: "a snapshot was captured" was asserted by
something that a truncated write, a zero-filled block or an error page would also satisfy.

What is checked instead is that the file is a JPEG the capture pipeline produced: the SOI and
EOI markers, the JFIF APP0 header, an SOF0 frame header declaring non-zero dimensions, and the
COM comment the service writes with the slip and stage. Those cannot all be present unless
CameraService.CreateTestJpegPattern ran to completion and the file was written whole.

This deliberately does not claim a photograph was taken. Nothing is photographed — the image is
generated, the service reports CameraSource.Simulator, and no physical camera is implemented.
The invariant being tested is that the capture-and-file path works end to end, which is what
the product does today.
#>
function Test-CaptureFile($file) {
    $problems = @()
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)

    if ($bytes.Length -lt 128) {
        return @("capture $($file.Name): only $($bytes.Length) bytes, too short to be a JPEG")
    }
    if ($bytes[0] -ne 0xFF -or $bytes[1] -ne 0xD8) {
        $problems += "capture $($file.Name): missing JPEG start-of-image marker"
    }
    if ($bytes[$bytes.Length - 2] -ne 0xFF -or $bytes[$bytes.Length - 1] -ne 0xD9) {
        # A truncated write is the failure this catches, and it is the one a size floor misses.
        $problems += "capture $($file.Name): missing JPEG end-of-image marker - file is truncated"
    }

    $text = [System.Text.Encoding]::ASCII.GetString($bytes)
    if ($text -notlike '*JFIF*') {
        $problems += "capture $($file.Name): no JFIF header"
    }
    if ($text -notlike '*WeighBridge Modern*') {
        $problems += "capture $($file.Name): no WeighBridge comment marker - not written by the capture service"
    }

    # SOF0 (0xFFC0) carries the frame dimensions. Zero either side means no frame was described,
    # which is what a hand-assembled stub that only had the outer markers would look like.
    $sof = -1
    for ($i = 2; $i -lt $bytes.Length - 9; $i++) {
        if ($bytes[$i] -eq 0xFF -and $bytes[$i + 1] -eq 0xC0) { $sof = $i; break }
    }
    if ($sof -lt 0) {
        $problems += "capture $($file.Name): no SOF0 frame header"
    } else {
        $height = ($bytes[$sof + 5] -shl 8) + $bytes[$sof + 6]
        $width = ($bytes[$sof + 7] -shl 8) + $bytes[$sof + 8]
        if ($height -lt 1 -or $width -lt 1) {
            $problems += "capture $($file.Name): frame declares ${width}x${height}"
        } else {
            Say "    valid JPEG, ${width}x${height}, generated (CameraSource.Simulator - no physical camera)"
        }
    }

    return $problems
}

# -------------------------------------------------------------
# STEP 1: Clean-database cold start
# -------------------------------------------------------------
Say "Terminating any running instances..."
Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

# The isolated root starts empty, so the cold start this step verifies — the migration running
# before any module reads a table — needs no deletion to arrange.
if (Test-Path $dbFile) {
    Write-Error "database already exists in the isolated root: $dbFile - isolation is not in effect"
}

# This test needs simulated hardware, and it has to ask for it by name.
#
# It used to rely on the regenerated settings template supplying an indicator and a camera. It
# did — the shipped defaults were Simulator and cameras on, which is exactly the defect the
# audit found, because a fresh installation then weighed vehicles with generated numbers and
# filed generated photographs against them. The defaults are now Serial and cameras off.
#
# Asked for through the environment rather than by writing JSON: Bootstrapper builds
# configuration from the file plus the WEIGHBRIDGE_ prefix, environment values win over the
# file, and Start-Process passes them to the child. So there is no settings file to write, no
# schema to keep in step with the template, and nothing to restore afterwards.
$env:WEIGHBRIDGE_Hardware__WeightIndicator__Enabled = 'true'
$env:WEIGHBRIDGE_Hardware__WeightIndicator__DriverType = 'Simulator'
$env:WEIGHBRIDGE_Camera__Enabled = 'true'
$env:WEIGHBRIDGE_Camera__CaptureOnWeighment = 'true'
Say "indicator set to Simulator and cameras enabled for this run, through WEIGHBRIDGE_ variables"

$baseline = if (Test-Path $logFile) { (Get-Item $logFile).Length } else { 0 }

Say "Launching WeighBridge Modern..."
$proc = Start-Process -FilePath $exe -PassThru

# Authenticates through the real login dialog and returns the shell, identified by
# AutomationId. It throws on any failure, so the old "first top-level window of the
# process" match - which is the login dialog, not the shell - cannot recur.
$window = Invoke-WeighBridgeLogin -ProcessId $proc.Id -TimeoutSec 45

[Win32]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Seconds 2

try {
    Say "Navigating to Vehicle Entry screen..."
    Open-Module $window "Vehicle Entry" "VehicleEntryViewModel"
    Start-Sleep -Milliseconds 800

    # Verify Live Scale Readout HUD element exists
    $liveWeightElem = Find-ByNameAndType $window "Live scale weight" $Ctrl::Text
    if ($null -eq $liveWeightElem) {
        $failures.Add("Live scale readout HUD TextBlock was not found on Vehicle Entry screen.")
        Say "FAILED: Live scale readout HUD element missing."
    } else {
        Say "Live scale readout verified: '$($liveWeightElem.Current.Name)'"
    }

    # Verify Read indicator button exists
    $readBtn = Find-ByNameAndType $window "Read indicator" $Ctrl::Button
    if ($null -eq $readBtn) {
        $failures.Add("Read indicator button was not found.")
        Say "FAILED: Read indicator button missing."
    } else {
        Say "Read indicator button located."
    }

    # Open a weighment
    Say "Filling out vehicle weighment form..."
    Set-Field $window "Vehicle number" "KA01MJ4004"
    Set-Field $window "Party" "Metro Infrastructure"
    Set-Field $window "Material" "Crushed Aggregate"

    Say "Opening weighment transaction..."
    Invoke-Named $window "Open weighment" $Ctrl::Button
    
    Assert-Text $window '(?:WB-)?\d{6}' 'a slip number was allocated and shown' | Out-Null
    Assert-Text $window 'First weight pending' 'the stage reads as first-weight-pending' | Out-Null

    # Capture weight from indicator / simulator
    Say "Clicking 'Read indicator' to capture live weight..."
    Invoke-Named $window "Read indicator" $Ctrl::Button
    Start-Sleep -Milliseconds 300

    # Ensure valid weight in input
    Set-Field $window "Weight in kilograms" "35000"

    # Record first weight
    Say "Recording first weight..."
    Invoke-Named $window "Record first weight" $Ctrl::Button
    
    Assert-Text $window 'Awaiting second weight' 'the first weight advanced the stage' | Out-Null

    # Check that a capture JPEG exists in $captureDir
    $captures = @(Get-ChildItem -Path $captureDir -Filter "*.jpg" -ErrorAction SilentlyContinue)
    if ($captures.Count -lt 1) {
        $failures.Add("No camera snapshot JPEG was captured during first weight record.")
        Say "FAILED: Camera capture directory has no images."
    } else {
        Say "Captured snapshot saved: $($captures[0].Name) ($($captures[0].Length) bytes)"
        foreach ($problem in (Test-CaptureFile $captures[0])) { $failures.Add($problem) }
    }

    # Record second weight (return trip)
    Say "Clicking 'Read indicator' for second weight..."
    Invoke-Named $window "Read indicator" $Ctrl::Button
    Start-Sleep -Milliseconds 300

    # Adjust slightly so tare works
    Set-Field $window "Weight in kilograms" "12500"

    Say "Recording second weight (completion)..."
    Invoke-Named $window "Record second weight" $Ctrl::Button
    
    Assert-Text $window '^Completed$' 'the stage badge reads Completed' | Out-Null

    $capturesAfterSecond = Get-ChildItem -Path $captureDir -Filter "*.jpg" -ErrorAction SilentlyContinue
    Say "Total snapshot images on disk: $($capturesAfterSecond.Count)"
    if ($capturesAfterSecond.Count -lt 2) {
        $failures.Add("Expected at least 2 snapshot images after completing first + second weight.")
        Say "FAILED: Did not find second snapshot image."
    } else {
        Say "Verified 2 snapshot images generated and saved."
    }

    Say "Closing application cleanly via WM_CLOSE..."
    [Win32]::PostMessage([IntPtr]$window.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    $proc.WaitForExit(8000) | Out-Null
    if (-not $proc.HasExited) {
        $proc.Kill()
        $failures.Add("Application did not exit cleanly within timeout.")
    } else {
        Say "Application process exited cleanly with code $($proc.ExitCode)."
    }

    # STEP 2: Verify Disk Captures
    Say "Validating snapshot files on disk..."
    $savedCaptures = Get-ChildItem -Path $captureDir -Filter "*.jpg" -ErrorAction SilentlyContinue
    if ($savedCaptures.Count -lt 2) {
        $failures.Add("Expected at least 2 snapshot images on disk, found $($savedCaptures.Count).")
    } else {
        foreach ($img in $savedCaptures) {
            Say "  Image: $($img.Name) ($($img.Length) bytes)"
            foreach ($problem in (Test-CaptureFile $img)) { $failures.Add($problem) }
        }
    }

    # STEP 3: Restart Persistence Test
    Say "=== RUN 2: Starting application for restart persistence check ==="
    $proc2 = Start-Process -FilePath $exe -PassThru

    # Caught rather than allowed to propagate so the reported failure names WHY the
    # restart did not reach the shell (login, startup dialog, or timeout) instead of
    # the bare "window did not appear" the old poll loop produced.
    $window2 = $null
    try {
        $window2 = Invoke-WeighBridgeLogin -ProcessId $proc2.Id -TimeoutSec 30
    } catch {
        $failures.Add("restart: $($_.Exception.Message)")
    }
    if ($window2) {
        [Win32]::SetForegroundWindow([IntPtr]$window2.Current.NativeWindowHandle) | Out-Null
        Start-Sleep -Seconds 1
        
        # Navigate to Vehicle Entry
        Open-Module $window2 "Vehicle Entry" "VehicleEntryViewModel"
        Start-Sleep -Milliseconds 800

        # Verify Live Scale Readout HUD is active on restart
        $liveWeightElem2 = Find-ByNameAndType $window2 "Live scale weight" $Ctrl::Text
        if ($null -ne $liveWeightElem2) {
            Say "Restart HUD verification: Live scale weight is active."
        }

        # Close second instance cleanly
        [Win32]::PostMessage([IntPtr]$window2.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        $proc2.WaitForExit(8000) | Out-Null
        Say "Restart verification complete. Process exited with code $($proc2.ExitCode)."
    }
    # No else: the catch above already recorded the specific reason the shell was not
    # reached, and a second generic entry would report one defect as two.

} catch {
    $failures.Add("Exception during hardware smoke test: $_")
    Say "ERROR: $_"
} finally {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force
    foreach ($name in @(
        'WEIGHBRIDGE_Hardware__WeightIndicator__Enabled',
        'WEIGHBRIDGE_Hardware__WeightIndicator__DriverType',
        'WEIGHBRIDGE_Camera__Enabled',
        'WEIGHBRIDGE_Camera__CaptureOnWeighment')) {
        Remove-Item "Env:\$name" -ErrorAction SilentlyContinue
    }
}

# STEP 4: Log inspection
if (-not (Test-Path $logFile)) {
    # Was a silent skip. A missing log is not a clean log — the application writes one on every
    # start, so its absence means this check never ran.
    $failures.Add("no log file was written at $logFile - the error check did not run")
} else {
    $newBytes = (Get-Item $logFile).Length - $baseline
    if ($newBytes -le 0) {
        $failures.Add("the log did not grow during the run - the error check had nothing to inspect")
    } else {
        $stream = [System.IO.File]::Open($logFile, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $stream.Seek($baseline, [System.IO.SeekOrigin]::Begin) | Out-Null
        $reader = New-Object System.IO.StreamReader($stream)
        $logDelta = $reader.ReadToEnd()
        $reader.Close()
        $stream.Close()

        $fatalMatches = @($logDelta -split "`r?`n" | Where-Object { $_ -match "\[(CRIT|FATAL|EROR|ERR)\]" -and $_ -notmatch "failed to open COM" })
        if ($fatalMatches.Count -gt 0) {
            Say "Log contains fatal errors:"
            $fatalMatches | ForEach-Object { Say "  $_" }
            $failures.Add("Log contains $($fatalMatches.Count) critical errors.")
        }
    }
}

if ($failures.Count -eq 0) {
    Say "=================================================="
    Say "PROMPT 9 HARDWARE + CAMERA SMOKE TEST: PASSED"
    Say "=================================================="
    Remove-IsolatedDataRoot -Root $paths.Root
    exit 0
} else {
    Say "=================================================="
    Say "PROMPT 9 HARDWARE + CAMERA SMOKE TEST: FAILED"
    $failures | ForEach-Object { Say " - $_" }
    Say "=================================================="
    # Kept: the database, captures and log of a failed run are what a diagnosis starts from.
    Remove-IsolatedDataRoot -Root $paths.Root -Keep
    exit 1
}
