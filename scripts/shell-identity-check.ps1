# Phase 6 evidence for F-021: the shell title bar must name the operator who signed in, not
# the Windows account the terminal runs under.
#
# A one-off runtime check rather than an assertion folded into another script, because it is
# the only thing here that reads the screen for an identity: the title bar binds
# MainWindowViewModel.CurrentUserName, and no unit test can reach it (WeighBridge.Tests does
# not reference WeighBridge.App - finding F-010).
#
# Runs against its own temporary data root, so the run creates its own first-run administrator
# instead of needing the operator's real password, and the live database is never opened.
#
# Exit 0 = the title bar named the operator. Exit 1 = it named something else.

param([string]$Operator = 'admin')

$ErrorActionPreference = 'Stop'

# Isolated data root: this run must never touch the operator's live database.
. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
# It starts empty, so first-run setup creates the administrator this check signs in as: there
# is no database to move aside and none to put back.
$data = New-IsolatedDataRoot -Label 'shell-identity-check'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'

function Say($text) { Write-Host "[identity] $text" }

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'login-helper.ps1')

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class IdentityWin32 {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

$found = @()

try {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force

    $proc = Start-Process -FilePath $exe -PassThru
    $shell = Invoke-WeighBridgeLogin -ProcessId $proc.Id -Username $Operator -TimeoutSec 60
    Say "signed in as '$Operator'"

    # Every static text in the shell, then the ones that name an account. The title bar's
    # TextBlock has no AutomationId (finding F-009), so it is identified by its content.
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Text)
    $texts = @($shell.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ })

    $found = @($texts | Where-Object { $_ -eq $Operator -or $_ -eq $env:USERNAME })
    Say "title-bar identity candidates: $($found -join ', ')"

    [IdentityWin32]::PostMessage(
        [IntPtr]$shell.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
    $proc.WaitForExit(20000) | Out-Null
}
finally {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-IsolatedDataRoot -Root $data.Root
}

# Checked in both directions: the operator has to be on screen, and the Windows account must
# not be - a check for only the first would pass while both were displayed.
if ($found -notcontains $Operator) {
    Say "FAIL: the shell never displayed '$Operator'"
    exit 1
}
if ($env:USERNAME -ne $Operator -and $found -contains $env:USERNAME) {
    Say "FAIL: the shell displayed the Windows account '$($env:USERNAME)'"
    exit 1
}

Say "PASS: the shell names the signed-in operator '$Operator', not the Windows account '$($env:USERNAME)'"
exit 0
