<#
.SYNOPSIS
    Compiles the production installer using Inno Setup.
#>

param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
$projectRoot = Split-Path -Parent $scriptDir
$issFile = Join-Path $scriptDir 'installer.iss'

function Say($text) { Write-Host "[installer] $text" -ForegroundColor Green }

if (-not $SkipPublish) {
    Say "Running publish-all.ps1 to build deployment bundles..."
    & (Join-Path $scriptDir 'publish-all.ps1')
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Publication failed! Cannot build installer."
        exit 1
    }
}

# Search for Inno Setup compiler (iscc.exe)
$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
) | Where-Object { $_ -and (Test-Path $_) }

if ($isccPaths.Count -eq 0) {
    Say "Inno Setup Compiler (ISCC.exe) was not detected. Attempting to install via winget..."
    try {
        winget install --id JRSoftware.InnoSetup -e --silent --accept-source-agreements --accept-package-agreements
        $isccPaths = @(
            "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            "C:\Program Files\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        ) | Where-Object { $_ -and (Test-Path $_) }
    } catch {
        # Fall through
    }
}

if ($isccPaths.Count -eq 0) {
    Say "Inno Setup Compiler (ISCC.exe) was not detected on this machine."
    Say "To compile the final .exe setup installer:"
    Say "  1. Download and install Inno Setup 6 from https://jrsoftware.org/isdl.php or run: winget install JRSoftware.InnoSetup"
    Say "  2. Run: ISCC.exe '$issFile'"
    Say "The published payload files are ready under 'publish/modern-win-x64' and 'publish/legacy-win-x86'."
    exit 0
}

$iscc = @($isccPaths) | Select-Object -First 1
Say "Compiling installer using $iscc..."
& "$iscc" $issFile


if ($LASTEXITCODE -eq 0) {
    Say "Installer successfully compiled to 'publish\installer'!"
    $setupFiles = Get-ChildItem -Path (Join-Path $projectRoot 'publish\installer') -Filter "*.exe" -ErrorAction SilentlyContinue
    foreach ($setup in $setupFiles) {
        $sizeMB = [math]::Round($setup.Length / 1MB, 2)
        $hash = (Get-FileHash -Path $setup.FullName -Algorithm SHA256).Hash
        Say "Generated Setup: $($setup.FullName)"
        Say "  File Size: $sizeMB MB"
        Say "  SHA256:    $hash"
    }
} else {
    Write-Error "Installer compilation failed."
    exit $LASTEXITCODE
}

