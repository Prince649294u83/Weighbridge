# Startup audit: clean-database cold start, existing-database start, and repeated cycles.
#
# Covers three of the audit's startup requirements in one run:
#   Phase 3  cycle 1 starts with no database at all, so the migration runs from nothing.
#   Phase 4  cycles 2..N start against the database cycle 1 created and migrated.
#   Phase 5  at least 10 full launch -> login -> shell -> shutdown cycles.
#
# Every cycle is judged outside the application's own self-reporting: the shell window
# appeared and named itself, a module was navigated to, the weight indicator connected and
# was released exactly once before the shutdown marker, the log delta contains no error or
# worse, the process exited on its own within the deadline, and its exit code was 0. Timing
# is recorded per cycle so a slow-drift or a first-run outlier is visible rather than
# averaged away.
#
# Each cycle opens Vehicle Entry, not the Dashboard, by pointing the operator's LastModule
# preference at it for the duration of the run. Vehicle Entry is the only module that calls
# IWeightIndicatorService.ConnectAsync, and a shutdown-ordering assertion that never saw a
# connected indicator cannot tell a fixed ordering apart from a log line that stopped being
# written at all.
#
# Cycle 1 also exercises first-run administrator setup, because the application seeds no
# account: a database with no users can only show the setup form. Every later cycle must
# sign that account in instead, which is asserted rather than assumed - setup running twice
# would mean the account never persisted.
#
# Every cycle then checks that the log names the operator who signed in, from the sign-in
# entry onwards, rather than the Windows account the terminal runs under. That was a real
# defect (F-020) and it is invisible to the unit tests, which cannot see how the container
# wired the loggers.
#
# No arbitrary sleeps. Every wait polls for the condition it is actually waiting for and
# fails on a deadline, because a sleep long enough to hide a race on this machine is not
# evidence about any other machine.
#
# Runs against its own temporary data root, which is deleted afterwards: the live database is
# never opened, let alone deleted. hardware-smoke.ps1 used to delete it outright, which
# destroyed real weighment data during this audit (finding F-012).
#
# Exit 0 = every cycle passed. Exit 1 = at least one cycle failed.

param([int]$Cycles = 10, [string]$Operator = 'admin')

$ErrorActionPreference = 'Stop'

# Isolated data root replaces the old stash-the-live-DB dance: nothing here can
# touch the operator's database any more.
. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$data = New-IsolatedDataRoot -Label 'startup-cycle-audit'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'
$logFile = $data.LogFile

function Say($text) { Write-Host "[startup] $text" }

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe" }
if ($Cycles -lt 10) { Write-Error "the audit requires at least 10 cycles; $Cycles was requested" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'login-helper.ps1')

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StartupWin32 {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

function Get-LogLength { if (Test-Path $logFile) { (Get-Item $logFile).Length } else { 0 } }

# Opened with FileShare.ReadWrite because the application still holds the file open.
function Get-LogDelta($from) {
    if (-not (Test-Path $logFile)) { return '' }
    $stream = [System.IO.File]::Open($logFile, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($from, 'Begin') | Out-Null
        return (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    } finally { $stream.Dispose() }
}

# Polls the cycle's own log delta. Returns $false on the deadline instead of throwing, so a
# missing line is reported as that cycle's failure and the remaining cycles still run.
function Wait-LogMatch($from, $pattern, $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ($true) {
        if ((Get-LogDelta $from) -match $pattern) { return $true }
        if ((Get-Date) -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 100
    }
}

$results = @()

try {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force

    # Cycle 1 must start from a clean database, which the temporary root supplies. Nothing here
    # is backed up or restored because nothing here belongs to the operator.
    [System.IO.File]::WriteAllText($data.PrefsFile,
        (@{ LastModule = 'VehicleEntry' } | ConvertTo-Json))
    Say 'startup module pointed at Vehicle Entry so the weight indicator connects'

    # And the simulator asked for by name, in this file, for the duration of the run. The
    # indicator assertions below used to work because "Simulator" was the product's default
    # DriverType - which is exactly the defect the audit found, since it meant a fresh
    # installation weighed vehicles with generated numbers. The default is now "Serial", so a
    # machine with no indicator on COM1 cannot reach Connected and these assertions would fail
    # for a reason that has nothing to do with startup. Configuring it here keeps the indicator
    # lifecycle covered and makes the dependency visible instead of inherited. The provisioner
    # merges in every key this omits.
    [System.IO.File]::WriteAllText($data.ConfigFile, (@{
        Hardware = @{ WeightIndicator = @{ Enabled = $true; DriverType = 'Simulator' } }
    } | ConvertTo-Json -Depth 10))
    Say 'weight indicator set to Simulator for the run'

    for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
        $kind = if ($cycle -eq 1) { 'clean database' } else { 'existing database' }
        $baseline = Get-LogLength
        $failures = @()

        $clock = [System.Diagnostics.Stopwatch]::StartNew()
        $proc = Start-Process -FilePath $exe -PassThru

        $shell = $null
        try {
            $shell = Invoke-WeighBridgeLogin -ProcessId $proc.Id -Username $Operator -TimeoutSec 60
        } catch {
            $failures += "reaching the shell: $($_.Exception.Message)"
        }
        $toShellMs = $clock.ElapsedMilliseconds

        # The shell being up is not the same as a module being up: the content area is
        # filled by InitializeAsync after the window is shown, so navigation is confirmed
        # from the log rather than from the window's existence.
        if ($shell) {
            if (-not (Wait-LogMatch $baseline 'Navigated to \w+ViewModel' 20)) {
                $failures += 'no module was navigated to within 20s of the shell opening'
            }
            elseif (-not (Wait-LogMatch $baseline 'weight indicator simulator stream' 20)) {
                # Simulator-specific because this run configured the simulator above. This
                # machine has no serial hardware, and the point of the assertion is the
                # indicator's start-up and teardown, not which driver is behind it.
                $failures += 'the weight indicator simulator did not start within 20s of Vehicle Entry opening'
            }
        }

        $exitCode = $null
        if ($shell) {
            [StartupWin32]::PostMessage(
                [IntPtr]$shell.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
            if ($proc.WaitForExit(20000)) {
                $exitCode = $proc.ExitCode
                if ($exitCode -ne 0) { $failures += "exit code $exitCode" }
            } else {
                $failures += 'did not exit within 20s of WM_CLOSE'
            }
        }
        $totalMs = $clock.ElapsedMilliseconds

        if (-not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) | Out-Null }

        $delta = Get-LogDelta $baseline
        $bad = $delta -split "`r?`n" | Where-Object { $_ -match '\[(ERR|EROR|CRIT|FATAL)\]' }
        foreach ($line in $bad) { $failures += "log: $line" }

        # The shutdown marker must be the last thing written. A line after it means
        # something was still running when the application said it had finished.
        $lines = @($delta -split "`r?`n" | Where-Object { $_.Trim() })
        $markerIndex = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match 'Shutdown complete') { $markerIndex = $i }
        }
        if ($markerIndex -ge 0 -and $markerIndex -lt ($lines.Count - 1)) {
            $trailing = $lines[($markerIndex + 1)..($lines.Count - 1)]
            $failures += "$($trailing.Count) log line(s) written after 'Shutdown complete'"
        }

        # Every entry from the sign-in onwards has to name the operator who signed in. This is
        # checked here and not only in the unit tests because the loggers take the shared
        # SignedInOperator as an optional constructor parameter: a registration that omitted it
        # would compile, resolve, pass every test, and quietly go back to stamping the Windows
        # account on the whole audit trail (finding F-020). Only the running container can show
        # that the wiring survived.
        $signInIndex = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match "User $Operator authenticated successfully") { $signInIndex = $i; break }
        }
        if ($signInIndex -lt 0) {
            $failures += "no 'User $Operator authenticated successfully' line, so who signed in cannot be judged"
        } else {
            # Continuation lines of a multi-line exception carry no scope, and the loggers
            # resolved as ILogger<T> are not enriched at all, so only lines that have a user
            # field are judged.
            $attributed = @($lines[$signInIndex..($lines.Count - 1)] | Where-Object { $_ -match 'user=(\S+?)[ }]' })
            $misattributed = @($attributed | Where-Object { $_ -match 'user=(\S+?)[ }]' -and $Matches[1] -ne $Operator })
            if ($misattributed.Count -gt 0) {
                $failures += "$($misattributed.Count) entry/entries after the sign-in name someone other than '$Operator', first: $($misattributed[0])"
            }
            elseif ($attributed.Count -eq 0) {
                # Otherwise dropping the user field from the enrichment would pass this check
                # by leaving nothing to judge. The sign-in entry itself is enriched, so there
                # is always at least one.
                $failures += 'no entry after the sign-in carries a user field at all'
            }
        }

        # First-run setup appoints the administrator exactly once, in cycle 1, which is the
        # only cycle that starts without a database. A later cycle reaching setup again would
        # mean the account was not persisted - which from the outside looks identical to a
        # perfectly healthy launch.
        $setup = @($lines | Where-Object { $_ -match 'created during first-run setup' })
        $expectedSetup = if ($cycle -eq 1) { 1 } else { 0 }
        if ($setup.Count -ne $expectedSetup) {
            $failures += "first-run setup ran $($setup.Count) time(s), expected $expectedSetup"
        }

        # The indicator is released once, by Bootstrapper.ShutdownAsync, before the marker.
        # Three container descriptors resolve to the one simulator instance and MEDI disposes
        # it once per resolved descriptor, so "exactly once" is the assertion that catches a
        # regression here - not "at least once". Anchored on the simulator's own logger name
        # because SystemStatusService reports a health state whose text is "Simulator
        # disconnected", and -match is case-insensitive.
        $released = @($lines | Where-Object { $_ -match 'WeightIndicatorSimulator :: Weight indicator simulator disconnected' })
        if ($released.Count -ne 1) {
            $failures += "the indicator reported disconnecting $($released.Count) time(s), expected 1"
        }
        elseif ($markerIndex -ge 0 -and [array]::IndexOf($lines, $released[0]) -gt $markerIndex) {
            $failures += "the indicator was released after 'Shutdown complete'"
        }

        $results += [pscustomobject]@{
            Cycle = $cycle; Kind = $kind; ToShellMs = $toShellMs; TotalMs = $totalMs
            ExitCode = $exitCode; Failures = $failures
        }

        $verdict = if ($failures.Count -eq 0) { 'ok' } else { "FAIL ($($failures.Count))" }
        Say ("cycle {0,2} [{1,-18}] shell {2,5} ms  total {3,5} ms  exit {4}  {5}" -f `
            $cycle, $kind, $toShellMs, $totalMs, $exitCode, $verdict)
        foreach ($failure in $failures) { Say "           - $failure" }
    }
}
finally {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-IsolatedDataRoot -Root $data.Root
}

$passed = @($results | Where-Object { $_.Failures.Count -eq 0 })
$shellTimes = @($results | ForEach-Object { $_.ToShellMs })

Write-Host ''
Say "cycles: $($results.Count)   passed: $($passed.Count)   failed: $($results.Count - $passed.Count)"
if ($shellTimes.Count -gt 0) {
    $stats = $shellTimes | Measure-Object -Minimum -Maximum -Average
    Say ("time to shell: min {0} ms  max {1} ms  mean {2} ms" -f `
        $stats.Minimum, $stats.Maximum, [int]$stats.Average)
}

if ($passed.Count -eq $results.Count) { exit 0 }

Say 'failed cycles:'
foreach ($result in $results | Where-Object { $_.Failures.Count -gt 0 }) {
    Say "  cycle $($result.Cycle) [$($result.Kind)]"
    foreach ($failure in $result.Failures) { Say "    - $failure" }
}
exit 1
