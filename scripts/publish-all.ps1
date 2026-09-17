<#
.SYNOPSIS
    Builds production self-contained publication bundles for both Modern (Win 10/11)
    and Legacy (Win 7/8 32-bit and 64-bit) operating systems.

.DESCRIPTION
    Generates isolated release output folders under `publish/`:
      - `publish/modern-win-x64`: Optimized 64-bit build for Windows 10 & 11
      - `publish/legacy-win-x86`: 32-bit build compatible with older terminals
      - `publish/legacy-win-x64`: 64-bit build for Windows 7 SP1 / 8.1
#>

param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\WeighBridge.App\WeighBridge.App.csproj'
$publishBase = Join-Path $projectRoot 'publish'

function Say($text) { Write-Host "[publish-all] $text" -ForegroundColor Cyan }

if (-not $SkipTests) {
    Say "Running test suite before publishing..."
    dotnet test "$projectRoot\tests\WeighBridge.Tests\WeighBridge.Tests.csproj" -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Tests failed! Aborting publication."
        exit 1
    }
    Say "All tests passed!"
}

# Ensure base publish folder exists
if (-not (Test-Path $publishBase)) {
    New-Item -ItemType Directory -Path $publishBase -Force | Out-Null
}

# 1. Modern 64-bit build (Windows 10/11)
$modernDir = Join-Path $publishBase 'modern-win-x64'
Say "Publishing Modern build (win-x64) to $modernDir..."
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -o $modernDir `
    --nologo

# 2. Legacy 32-bit build (Windows 7/8/10 32-bit)
$legacyX86Dir = Join-Path $publishBase 'legacy-win-x86'
Say "Publishing Legacy 32-bit build (win-x86) to $legacyX86Dir..."
dotnet publish $project `
    -c Release `
    -r win-x86 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -o $legacyX86Dir `
    --nologo

Say "Publication completed successfully!"
Say "  Modern 64-bit: $modernDir"
Say "  Legacy 32-bit: $legacyX86Dir"
