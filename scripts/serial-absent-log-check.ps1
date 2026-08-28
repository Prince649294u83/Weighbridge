# Verifies the shipped hardware default is quiet when it fails: with DriverType Serial on a
# machine with no indicator on COM1, the log must report the failure once rather than once per
# retry. Before the repair, each attempt wrote six lines every three seconds - roughly 170,000
# lines a day - which rolls the 20 MB log in hours and destroys the thirty days of history the
# site is configured to keep. Counts what actually lands in the log over a 40-second run.
#
# Runs against its own temporary data root, so the operator's database, configuration and log
# history are never opened. First-run setup creates the administrator the login helper then
# signs in as, which is why no credential is needed or stored here.

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$data = New-IsolatedDataRoot -Label 'serial-absent-log-check'

$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'login-helper.ps1')

try {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force

    # Written before the first launch. The provisioner merges in every key this omits, so only
    # the three that matter to the test are stated - and because the root is temporary, there
    # is nothing to back up and nothing to restore.
    [System.IO.File]::WriteAllText($data.ConfigFile, (@{
        Hardware = @{ WeightIndicator = @{ Enabled = $true; DriverType = 'Serial'; PortName = 'COM1' } }
    } | ConvertTo-Json -Depth 10))

    [System.IO.File]::WriteAllText($data.PrefsFile, (@{ LastModule = 'VehicleEntry' } | ConvertTo-Json))

    $proc = Start-Process -FilePath $exe -PassThru
    $shell = Invoke-WeighBridgeLogin -ProcessId $proc.Id -TimeoutSec 60
    if (-not $shell) { throw 'the shell never came up' }

    Write-Host "[serial] shell up, holding Vehicle Entry open for 40s of reconnect attempts"
    Start-Sleep -Seconds 40
    $proc.Kill(); $proc.WaitForExit(5000) | Out-Null

    # A missing log is a failure, not a pass: the whole measurement is the log's contents, and
    # a guard that skipped the assertion when the file was absent would report success for a
    # run that produced no evidence at all.
    if (-not (Test-Path $data.LogFile)) {
        throw "no log file was written at $($data.LogFile); there is nothing to measure"
    }

    $stream = [System.IO.File]::Open($data.LogFile, 'Open', 'Read', 'ReadWrite')
    try {
        $delta = (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    } finally { $stream.Dispose() }

    $lines = @($delta -split "`r?`n")
    Write-Host ("[serial] lines written: {0}" -f $lines.Count)
    foreach ($pattern in 'Failed to open serial port', 'Connecting to serial transport',
                         'Waiting \d+ms before reconnecting', 'Serial connection lost or failed',
                         'Opening serial port') {
        Write-Host ("[serial] {0,-38} {1}" -f $pattern, @($lines | Where-Object { $_ -match $pattern }).Count)
    }
    Write-Host ("[serial] [ERR] lines: {0}" -f @($lines | Where-Object { $_ -match '\[ERR\]' }).Count)
    foreach ($line in @($lines | Where-Object { $_ -match '\[(ERR|WARN)\]' })) { Write-Host "[serial]   $line" }

    # Thirteen or so attempts fit in forty seconds. One report of each is the fix; anything
    # near one per attempt is the defect back again. Errors that are not about the serial port
    # count too - a run that fills the log with something else is not a passing run.
    $script:errorCount = @($lines | Where-Object { $_ -match '\[(ERR|EROR|CRIT|FATAL)\]' }).Count
    $script:attempts = @($lines | Where-Object { $_ -match 'Failed to open serial port' }).Count
}
finally {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-IsolatedDataRoot -Root $data.Root
}

if ($script:errorCount -le 1 -and $script:attempts -le 1) {
    Write-Host '[serial] ok: the absent port was reported once'
    exit 0
}

Write-Host ("[serial] FAIL: {0} error line(s), {1} report(s) of the absent port" -f `
    $script:errorCount, $script:attempts)
exit 1
