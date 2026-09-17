# Builds the installable application and puts a shortcut on the Desktop.
#
# Run this whenever you want the double-clickable app to catch up with the source:
#
#     powershell -ExecutionPolicy Bypass -File scripts\publish-app.ps1
#
# It is self-contained (-r win-x64 --self-contained), so the published folder carries its
# own .NET 8 runtime and runs on a site PC with nothing installed. That is the point of a
# weighbridge terminal: no "please install the .NET Desktop Runtime" phone call.
#
# Not PublishSingleFile, deliberately. In single-file mode Assembly.Location is empty, and
# ApplicationInfoService falls back to DateTime.Now for the build date - so every terminal
# would report today as its build date and there would be no way to tell which build is
# running. A folder plus a shortcut is a double-click either way.
#
# The app keeps its data in %LOCALAPPDATA%\WeighBridge Modern (database, appsettings.json,
# logs, captures, reports). Republishing does not touch any of it, and the published build
# and a debug build share it - the same database, the same users, the same settings.

param(
    # Stop a running instance instead of refusing. Without this the script will not
    # interrupt work in progress.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\WeighBridge.App\WeighBridge.App.csproj'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\WeighBridge Modern'
$exe = Join-Path $installDir 'WeighBridge.App.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'WeighBridge.lnk'

function Say($text) { Write-Host "[publish] $text" }

# A running instance holds its own files open, so the copy would half-finish and leave a
# broken install behind. Refuse by default: the operator may be mid-weighment.
$running = Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue
if ($running) {
    if (-not $Force) {
        Write-Host ''
        Say "WeighBridge is running (pid $($running.Id -join ', ')). Close it and run this again,"
        Say 'or pass -Force to stop it now. Not stopping it for you in case a weighment is open.'
        exit 1
    }
    Say "stopping the running app (pid $($running.Id -join ', '))"
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}

Say "building Release from $project"
if (Test-Path $installDir) {
    Get-ChildItem $installDir -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $installDir `
    --nologo
if ($LASTEXITCODE -ne 0) { Say 'the build failed - nothing was replaced'; exit 1 }

if (-not (Test-Path $exe)) { Say "the build reported success but $exe is missing"; exit 1 }

# Refreshed every run across all desktop locations
$shell = New-Object -ComObject WScript.Shell
$desktopDirs = @(
    [Environment]::GetFolderPath('Desktop'),
    [Environment]::GetFolderPath('CommonDesktopDirectory'),
    "C:\Users\dell\Desktop",
    "C:\Users\dell\OneDrive\Desktop"
) | Select-Object -Unique

foreach ($dir in $desktopDirs) {
    if (Test-Path $dir) {
        $shortcutPath = Join-Path $dir 'WeighBridge Modern.lnk'
        $link = $shell.CreateShortcut($shortcutPath)
        $link.TargetPath = $exe
        $link.WorkingDirectory = $installDir
        $link.IconLocation = $exe
        $link.Description = 'WeighBridge Modern Application'
        $link.Save()
    }
}

$size = [math]::Round(((Get-ChildItem $installDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
$built = (Get-Item $exe).LastWriteTime

Write-Host ''
Say 'done.'
Say "  app       $exe"
Say "  shortcut  $shortcut  (double-click this)"
Say "  built     $built   |  $size MB, runtime included"
Say "  data      $(Join-Path $env:LOCALAPPDATA 'WeighBridge Modern')  (untouched by this script)"
