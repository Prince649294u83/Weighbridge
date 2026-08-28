# Final Application End-to-End Smoke Test for WeighBridge Modern
#
# Verifies:
# 1. Solution build and a 100% unit/integration test pass
# 2. Application startup reaching the blocking login dialog
# 3. Authentication through the real dialog, and the shell opening
# 4. The shell STAYS open - regression guard for the startup shutdown defect where
#    the login dialog became Application.MainWindow under OnMainWindowClose, so the
#    process tore itself down milliseconds after displaying the shell
# 5. Clean shutdown, exit code 0, and the shutdown marker reaching the log
#
# Navigation is not asserted here; runtime-smoke.ps1 covers it with log-count deltas.

param(
    # Verify an application that is already built, instead of building one. Used to audit
    # the published Release build: steps 1 and 2 compile and test the source tree, which
    # is not what is under test once a binary exists.
    #
    # Named ExePath, not Exe: PowerShell variables are case-insensitive, so a parameter
    # named $Exe would be the same variable as the $exe holding the resolved path.
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = if ($ExePath) { $ExePath } else {
    Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'
}
# Isolated data root: this run must never touch the operator's live database or log.
. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$data = New-IsolatedDataRoot -Label 'final-smoke'
$logFile = $data.LogFile

# How long the shell must survive after login before the run is believed. The defect
# this guards killed the process about 2 ms after the shell appeared.
$shellMustSurviveSec = 5

function Say($text) { Write-Host "[final-smoke] $text" -ForegroundColor Cyan }
function Pass($text) { Write-Host "[PASS] $text" -ForegroundColor Green }
function Fail($text) { Write-Host "[FAIL] $text" -ForegroundColor Red; throw $text }

if (-not (Test-Path $exe)) { Fail "No application at $exe" }
Say "application under test: $exe"

if ($ExePath) {
    Say '==== Steps 1-2 skipped: verifying a prebuilt application ===='
}
else {
    Say "==== Step 1: Running Automated Test Pass ===="
    Set-Location $projectRoot
    & dotnet test --nologo
    if ($LASTEXITCODE -ne 0) {
        Fail "dotnet test failed!"
    }
    Pass "All automated tests passed successfully."

    Say "==== Step 2: Building App executable ===="
    & dotnet build (Join-Path $projectRoot 'src\WeighBridge.App\WeighBridge.App.csproj') -c Debug
    if ($LASTEXITCODE -ne 0) {
        Fail "App build failed!"
    }
    Pass "App build completed successfully."
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'login-helper.ps1')

$proc = $null
try {
    Say "==== Step 3: Launching Application & Testing Login ===="
    $baseline = if (Test-Path $logFile) { (Get-Content $logFile).Count } else { 0 }

    # $pid is a read-only PowerShell automatic variable; assigning it aborts the script.
    $proc = Start-Process -FilePath $exe -PassThru
    $processId = $proc.Id
    Say "Process started with PID: $processId"

    $shell = Invoke-WeighBridgeLogin -ProcessId $processId
    Pass "Authenticated, and the shell opened: '$($shell.Current.Name)'"

    Say "==== Step 4: Confirming the shell survives (startup shutdown regression) ===="
    Start-Sleep -Seconds $shellMustSurviveSec

    if ($proc.HasExited) {
        Fail "Process exited $shellMustSurviveSec s after the shell opened (exit code $($proc.ExitCode)). The application is shutting itself down during startup."
    }

    $stillThere = Get-WeighBridgeWindow -ProcessId $processId -AutomationId 'ShellWindow' -TimeoutSec 2
    if (-not $stillThere) {
        Fail "The shell window disappeared within $shellMustSurviveSec s of opening."
    }

    # Checked after the settle, not before: the startup module loads asynchronously, so a
    # module that throws raises its dialog a moment after the shell is first visible.
    $startupError = Get-WeighBridgeStartupError -ProcessId $processId
    if ($startupError) {
        Fail "The application is showing '$startupError' - a module failed to load during startup."
    }
    Pass "Shell still present, no startup error dialog, process alive after $shellMustSurviveSec s."

    Say "==== Step 5: Clean shutdown ===="
    $stillThere.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()

    if (-not $proc.WaitForExit(15000)) {
        Fail "Application did not exit within 15s of closing the shell."
    }
    if ($proc.ExitCode -ne 0) {
        Fail "Application exited with code $($proc.ExitCode), expected 0."
    }
    Pass "Application exited cleanly (code 0)."

    if (-not (Test-Path $logFile)) {
        Fail "No log file was written at $logFile."
    }
    # Compared by line, not by byte offset: the log is UTF-8 with a BOM, so a byte
    # length is not a character index into what Get-Content returns.
    $appended = (Get-Content $logFile | Select-Object -Skip $baseline) -join "`n"
    if ($appended -notmatch 'Shutdown complete') {
        Fail "This run appended no 'Shutdown complete' marker to the log."
    }
    Pass "Shutdown sequence recorded in application log."

    if ($appended -match '\[(ERR|FTL)\]') {
        Fail "This run appended error-level log entries:`n$($Matches[0])"
    }
    Pass "No error-level log entries during the run."

    Write-Host ""
    Pass "ALL SMOKE TEST CRITERIA MET!"
    exit 0
}
catch {
    Write-Host ""
    Write-Host "[final-smoke] --- FAILED --- $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if ($proc -and -not $proc.HasExited) {
        $proc.Kill()
        Write-Host "[final-smoke] Killed leftover process $($proc.Id)." -ForegroundColor Yellow
    }
    Remove-IsolatedDataRoot -Root $data.Root
}
