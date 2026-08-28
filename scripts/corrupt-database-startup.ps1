# Phase 4 of the audit: startup against a database file that exists and is broken.
#
# The absent-database case is covered by startup-cycle-audit.ps1 cycle 1, and the healthy
# existing-database case by cycles 2..N. Neither says anything about the case a weighbridge
# terminal actually meets: a file that is present, that the application will happily open, and
# whose contents are damaged - an interrupted copy, a disk that filled mid-write, a backup
# restored from the wrong place, or a file some other program overwrote.
#
# Three variants, each a realistic failure rather than a random mutation:
#   garbage    the file is not a database at all
#   truncated  a real database cut in half, so the header is valid and the pages are not
#   empty      a zero-byte file, which is what an interrupted create leaves behind
#
# What is being judged, for each variant:
#   1. The application does not silently vanish - something is on screen.
#   2. It does not reach the shell, because the shell over a broken database is a terminal
#      that looks like it is working.
#   3. The failure is written to the log, so the site can tell what happened.
#   4. The broken file is still there afterwards. An application that "repairs" a database it
#      cannot read by replacing it has destroyed whatever a specialist might have recovered.
#   5. The dialog names the database file. A dialog titled correctly over an empty body is a
#      check that passes while the operator still has nothing to act on.
#
# The damage is done to a database inside a temporary data root, which is deleted at the end.
# The live database is never opened - and never deleted, which is finding F-012.
#
# Exit 0 = every variant handled acceptably. Exit 1 = at least one did not.

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'

. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$data = New-IsolatedDataRoot -Label 'corrupt-database-startup'
$dbFile = $data.DbFile
$logFile = $data.LogFile

function Say($text) { Write-Host "[corrupt] $text" }

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'login-helper.ps1')

function Get-LogLength { if (Test-Path $logFile) { (Get-Item $logFile).Length } else { 0 } }

function Get-LogDelta($from) {
    if (-not (Test-Path $logFile)) { return '' }
    $stream = [System.IO.File]::Open($logFile, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($from, 'Begin') | Out-Null
        return (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    } finally { $stream.Dispose() }
}

# Every top-level window the process is showing, by title, so the report says what the
# operator would actually have seen rather than "it failed".
function Get-WindowTitles($processId) {
    $element = [System.Windows.Automation.AutomationElement]
    try {
        return @($element::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition(
                $element::ProcessIdProperty, $processId))) |
            ForEach-Object { $_.Current.Name })
    } catch { return @() }
}

# A real database to cut in half. Built by the application itself in a scratch directory, so
# the truncated variant starts from the genuine schema rather than a hand-made file.
function New-RealDatabase {
    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("wb-corrupt-" + [System.Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    $source = Join-Path $scratch 'seed.db'

    # Launch once against the empty temporary root and let first-run behaviour migrate a
    # database, then take that. This used to prefer a copy of the operator's own stashed
    # database, which is no longer reachable and was never needed: the schema is the same one
    # either way, and it comes from the migrations rather than from any site's data.
    $proc = Start-Process -FilePath $exe -PassThru
    $null = Get-WeighBridgeWindow -ProcessId $proc.Id -Title $script:SetupWindowTitle -TimeoutSec 60
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Milliseconds 500
    Copy-Item $dbFile $source
    Remove-Item $dbFile -Force
    return $source
}

$variants = @()
$results = @()

try {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force

    $realDb = New-RealDatabase
    $realBytes = [System.IO.File]::ReadAllBytes($realDb)
    Say ("a real database to damage: {0} bytes" -f $realBytes.Length)

    $variants = @(
        @{ Name = 'garbage'; Write = { [System.IO.File]::WriteAllText($dbFile, 'this is not a database, it is a text file') } }
        @{ Name = 'truncated'; Write = { [System.IO.File]::WriteAllBytes($dbFile, $realBytes[0..([int]($realBytes.Length / 2))]) } }
        @{ Name = 'empty'; Write = { [System.IO.File]::WriteAllBytes($dbFile, [byte[]]@()) } }
    )

    foreach ($variant in $variants) {
        $failures = @()
        & $variant.Write
        $written = (Get-Item $dbFile).Length
        $writtenHash = (Get-FileHash $dbFile -Algorithm SHA256).Hash
        $baseline = Get-LogLength

        $proc = Start-Process -FilePath $exe -PassThru

        # Long enough for the startup path to reach either a dialog or a shell, polled for
        # rather than slept through: the outcome is whichever appears first.
        $deadline = (Get-Date).AddSeconds(45)
        $titles = @()
        $shell = $null
        $errorDialog = $null
        try {
            while ((Get-Date) -lt $deadline) {
                if ($proc.HasExited) { break }
                $shell = Get-WeighBridgeWindow -ProcessId $proc.Id -AutomationId $script:ShellAutomationId -TimeoutSec 0
                $errorDialog = Get-WeighBridgeStartupError -ProcessId $proc.Id
                $titles = @(Get-WindowTitles $proc.Id)
                if ($shell -or $errorDialog) { break }
                # A login or setup form is an outcome too, and a finding in itself.
                if ($titles -contains $script:LoginWindowTitle -or $titles -contains $script:SetupWindowTitle) { break }
                Start-Sleep -Milliseconds 250
            }
        }
        catch {
            # Reported as this variant's failure rather than aborting the run, so the other
            # variants are still exercised and the database still gets restored.
            $failures += "the observation itself threw: $($_.Exception.Message)"
        }

        $titles = @(Get-WindowTitles $proc.Id)
        $exited = $proc.HasExited
        $delta = Get-LogDelta $baseline

        if ($shell) { $failures += 'reached the shell over a database it could not read' }

        if ($exited -and -not $errorDialog) {
            $failures += "the process exited (code $($proc.ExitCode)) without showing anything"
        }
        elseif (-not $errorDialog -and @($titles | Where-Object { $_ }).Count -eq 0) {
            $failures += 'no window of any kind appeared within 45s'
        }

        if ($delta -notmatch '(?i)database|sqlite|corrupt|not a database|malformed|disk image') {
            $failures += 'the log delta says nothing about the database'
        }

        # What the operator can act on is the dialog body, not its title, so the body is read
        # back rather than assumed from the title being right.
        $body = ''
        if ($errorDialog) {
            $dialog = Get-WeighBridgeWindow -ProcessId $proc.Id -Title $errorDialog -TimeoutSec 0
            $body = if ($dialog) {
                @($dialog.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                        [System.Windows.Automation.ControlType]::Text))) |
                    ForEach-Object { $_.Current.Name }) -join ' '
            } else { '' }

            if ($body -notmatch '(?i)weighbridge\.db') {
                $failures += "the dialog does not name the database file: '$body'"
            }
        }

        # Ended before the file is examined: the application keeps the database open, so
        # hashing it while the process lives fails on the file lock rather than on anything
        # about the file.
        if (-not $proc.HasExited) {
            if ($shell) {
                [void]$proc.CloseMainWindow()
            }
            $proc.Kill()
            $proc.WaitForExit(5000) | Out-Null
        }

        if (-not (Test-Path $dbFile)) {
            $failures += 'the damaged file was deleted'
        }
        else {
            $after = (Get-Item $dbFile).Length
            if ((Get-FileHash $dbFile -Algorithm SHA256).Hash -ne $writtenHash) {
                $failures += "the damaged file was rewritten ($written -> $after bytes)"
            }
        }

        $shown = if ($errorDialog) { "error dialog '$errorDialog'" }
                 elseif ($shell) { 'the shell' }
                 elseif (@($titles | Where-Object { $_ }).Count) { "window(s): $((@($titles | Where-Object { $_ })) -join ', ')" }
                 else { 'nothing' }

        $results += [pscustomobject]@{
            Variant = $variant.Name; Bytes = $written; Shown = $shown; Dialog = $body
            Exited = $exited; Failures = $failures
            LogLines = @($delta -split "`r?`n" | Where-Object { $_ -match '(?i)\[(ERR|EROR|WARN|CRIT|FATAL)\]' })
        }

        $verdict = if ($failures.Count -eq 0) { 'ok' } else { "FAIL ($($failures.Count))" }
        Say ("{0,-10} {1,8} bytes -> showed {2}  {3}" -f $variant.Name, $written, $shown, $verdict)
        foreach ($failure in $failures) { Say "           - $failure" }

        Remove-Item $dbFile -Force -ErrorAction SilentlyContinue
    }

    Remove-Item (Split-Path -Parent $realDb) -Recurse -Force -ErrorAction SilentlyContinue
}
finally {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-IsolatedDataRoot -Root $data.Root
}

Write-Host ''
foreach ($result in $results) {
    Say "$($result.Variant): showed $($result.Shown)"
    if ($result.Dialog) { Say "    dialog text: $($result.Dialog)" }
    foreach ($line in $result.LogLines) { Say "    $line" }
}

$bad = @($results | Where-Object { $_.Failures.Count -gt 0 })
Write-Host ''
Say "variants: $($results.Count)   passed: $($results.Count - $bad.Count)   failed: $($bad.Count)"
if ($bad.Count -eq 0) { exit 0 }

Say 'failed variants:'
foreach ($result in $bad) {
    Say "  $($result.Variant)"
    foreach ($failure in $result.Failures) { Say "    - $failure" }
}
exit 1
