# Gives a smoke script its own data root, so nothing it does can reach the operator's.
#
# Dot-source this, call New-IsolatedDataRoot, and use the paths it returns. The application
# picks the root up from WEIGHBRIDGE_DATA_ROOT, which Start-Process inherits, so the launched
# process writes its configuration, database, logs, captures and reports inside the temporary
# folder and the real installation is never opened at all.
#
# This exists because the alternative was worse in a way that cost real data. Three scripts
# used to delete the database at the real path to test a cold start; run against a live
# installation, that destroyed the operator's weighment history, and no amount of restore
# logic in a finally block makes deleting live data an acceptable way to arrange a test
# fixture. Isolation removes the need to be careful rather than asking the next script author
# to be.

$ErrorActionPreference = 'Stop'

<#
.SYNOPSIS
Creates a fresh, empty data root and points the application at it for this session.

.DESCRIPTION
Returns an object carrying every path the scripts previously derived from LOCALAPPDATA, so a
script converts by replacing its path block with one call. The root is created empty: a caller
that wants a cold start needs no deletion, and a caller that wants existing data seeds it.

.PARAMETER Label
Included in the folder name to make a leftover root identifiable in a temp listing.

.PARAMETER SeedFrom
Optional existing data root to copy in, for scripts that need to exercise an upgrade or a
populated database. Copied, never moved, so the source is left untouched.
#>
function New-IsolatedDataRoot {
    param(
        [Parameter(Mandatory)][string]$Label,
        [string]$SeedFrom
    )

    $stamp = (Get-Date -Format 'yyyyMMdd-HHmmss')
    $container = Join-Path ([System.IO.Path]::GetTempPath()) 'WeighBridge.Smoke'
    $root = Join-Path $container "$Label-$stamp-$PID"

    Assert-NotLiveDataRoot -Path $root

    # A run that fails part way through keeps its root, because the log and the database of a
    # failed run are the evidence for diagnosing it. Pruning on the way in rather than on the
    # way out is what stops those accumulating, and it needs no cleanup handler in the caller.
    if (Test-Path $container) {
        Get-ChildItem $container -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-1) } |
            ForEach-Object { Remove-Item $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    }

    if ($SeedFrom) {
        if (-not (Test-Path $SeedFrom)) { throw "SeedFrom does not exist: $SeedFrom" }
        Copy-Item -Path $SeedFrom -Destination $root -Recurse -Force
    }

    foreach ($sub in @('', 'Data', 'Logs', 'Captures', 'Reports')) {
        $null = New-Item -ItemType Directory -Force -Path (Join-Path $root $sub)
    }

    # Inherited by every child process this script starts. Set before the first launch, or the
    # application resolves the real root and the isolation is silently not in effect.
    $env:WEIGHBRIDGE_DATA_ROOT = $root

    return [pscustomobject]@{
        Root       = $root
        DataDir    = Join-Path $root 'Data'
        DbFile     = Join-Path $root 'Data\weighbridge.db'
        LogDir     = Join-Path $root 'Logs'
        LogFile    = Join-Path $root ('Logs\weighbridge-{0:yyyy-MM-dd}.log' -f (Get-Date))
        CaptureDir = Join-Path $root 'Captures'
        ReportDir  = Join-Path $root 'Reports'
        ConfigFile = Join-Path $root 'appsettings.json'
        PrefsFile  = Join-Path $root 'userpreferences.json'
    }
}

<#
.SYNOPSIS
Throws if a path is, contains, or sits inside the live installation's data root.

.DESCRIPTION
The backstop for the whole arrangement. Any script that is about to delete something calls
this first, so a mistyped variable, an unset environment variable or a copied-and-pasted path
block fails loudly instead of removing the operator's database.

Both directions are checked. A path inside the live root is refused because deleting it
destroys live data; the live root inside the path is refused because deleting a parent takes
the live root with it.
#>
function Assert-NotLiveDataRoot {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'Refusing to operate on an empty path.'
    }

    $live = Join-Path $env:LOCALAPPDATA 'WeighBridge Modern'

    # Normalised without requiring existence: the target of a delete may already be gone, and
    # GetFullPath resolves '..' segments that would otherwise walk out of a temp folder.
    $normalise = { param($p) [System.IO.Path]::GetFullPath($p).TrimEnd('\', '/') }
    $candidate = & $normalise $Path
    $liveFull = & $normalise $live

    if ($candidate.Equals($liveFull, [StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith($liveFull + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $liveFull.StartsWith($candidate + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "REFUSED: '$Path' is the live installation data root ('$live') or contains it. " +
              'A smoke test must run against an isolated root from New-IsolatedDataRoot.'
    }
}

<#
.SYNOPSIS
Deletes an isolated data root, after proving it is not the live one.

.DESCRIPTION
Safe to call from a finally block: it never throws for a root that is already gone, and it
does not mask an in-flight failure with a cleanup error. It does still throw for a path that
fails the live-root check, because that is a defect in the script rather than untidiness.

.PARAMETER Keep
Leaves the folder in place, for diagnosing a failed run. The path is printed either way.
#>
function Remove-IsolatedDataRoot {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Root,
        [switch]$Keep
    )

    Assert-NotLiveDataRoot -Path $Root
    Remove-Item 'Env:\WEIGHBRIDGE_DATA_ROOT' -ErrorAction SilentlyContinue

    if ($Keep) {
        Write-Host "[isolated-root] kept for inspection: $Root"
        return
    }

    # Retried: the application was very likely killed moments ago and Windows can still hold
    # the SQLite handle. Failing to clean a temp folder is not worth failing a run over.
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            Remove-Item -LiteralPath $Root -Recurse -Force -ErrorAction Stop
            return
        } catch {
            if ($attempt -eq 10) {
                Write-Host "[isolated-root] could not remove $Root ($($_.Exception.Message)); left on disk"
                return
            }
            Start-Sleep -Milliseconds 300
        }
    }
}
