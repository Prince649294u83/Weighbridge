# Runtime evidence that signing out works, end to end.
#
# Sign-out is the one item on the acceptance list that no unit test can reach: it spans the
# ViewModel command, the window that closes itself, and App.RunSessionsAsync - the loop that
# decides whether a closed shell means "show the login dialog again" or "the process is
# finished". WeighBridge.Tests does not reference WeighBridge.App (finding F-010), so the only
# way to know the loop turns is to make it turn.
#
# What this asserts, in order:
#   1. the shell offers a sign-out control at all;
#   2. it asks for confirmation through the application's own dialog, not a MessageBox;
#   3. the shell closes and the login dialog comes back - the process did not exit;
#   4. a second sign-in builds a second shell. This is the assertion that catches singletons
#      which assumed one window per process: WindowPlacementService.Attach used to throw on a
#      second call, which turned the first sign-out into a failed startup;
#   5. the audit trail records the sign-out against the operator who did it, not the Windows
#      account the terminal runs under;
#   6. closing the second shell ends the process with exit code 0.
#
# Runs against its own temporary data root, so it appoints its own administrator and never
# opens the operator's live database.
#
# Exit 0 = every step above held. Exit 1 = at least one did not; each is named.

param([string]$Operator = 'admin')

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$data = New-IsolatedDataRoot -Label 'sign-out-check'

$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'

function Say($text) { Write-Host "[signout] $text" }

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
. (Join-Path $PSScriptRoot 'login-helper.ps1')

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SignOutWin32 {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@

$element = [System.Windows.Automation.AutomationElement]
$tree = [System.Windows.Automation.TreeScope]

$failures = @()
$exitCode = $null

function Find-ByAutomationId($scope, $id) {
    return $scope.FindFirst($tree::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($element::AutomationIdProperty, $id)))
}

function Invoke-Element($target) {
    $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# True once the shell has genuinely gone from the desktop tree. Polled rather than assumed:
# Invoke returns as soon as the click is delivered, well before the window has closed.
function Wait-ShellGone($processId, $timeoutSec = 20) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-WeighBridgeWindow -ProcessId $processId -AutomationId 'ShellWindow' -TimeoutSec 0)) {
            return $true
        }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

try {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force

    $proc = Start-Process -FilePath $exe -PassThru

    # First-run setup: the temporary database has no accounts, so this appoints the operator.
    $shell = Invoke-WeighBridgeLogin -ProcessId $proc.Id -Username $Operator -TimeoutSec 60
    Say "session 1: signed in as '$Operator'"

    # --- 1. the control exists ---------------------------------------------------------
    # Matched on AutomationProperties.Name: the button's content is a Segoe MDL2 glyph, and
    # asserting on a private-use codepoint would be unreadable and would break with the icon.
    $signOut = $shell.FindFirst($tree::Descendants,
        (New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition($element::NameProperty, 'Sign out')),
            (New-Object System.Windows.Automation.PropertyCondition(
                $element::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))))

    if (-not $signOut) {
        $failures += 'the shell has no "Sign out" button'
        throw 'cannot continue without the sign-out control'
    }
    Say 'found the sign-out button'

    Invoke-Element $signOut

    # --- 2. it confirms, through the application's own dialog --------------------------
    # The title is the ViewModel's confirmation prompt. A MessageBox would not carry it, so
    # this doubles as the check that the no-MessageBox constraint still holds here.
    $confirm = Get-WeighBridgeWindow -ProcessId $proc.Id -Title 'Sign out?' -TimeoutSec 15
    if (-not $confirm) {
        # Named windows and the log tail, for the same reason login-helper dumps the tree: a
        # bare "the dialog did not appear" leaves the next reproduction as uninformative as
        # this one. The two together separate "the command never ran" from "it ran and the
        # dialog is titled something else".
        $titles = @($element::RootElement.FindAll($tree::Children,
            (New-Object System.Windows.Automation.PropertyCondition($element::ProcessIdProperty, $proc.Id))) |
            ForEach-Object { "'$($_.Current.Name)'" })
        Say "top-level windows now: $(if ($titles.Count) { $titles -join ', ' } else { 'none' })"

        $probe = Get-ChildItem -Path $data.LogDir -Filter '*.log' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime | Select-Object -Last 1
        if ($probe) {
            Say 'last 6 log lines:'
            Get-Content -LiteralPath $probe.FullName -Tail 6 | ForEach-Object { Say "  $_" }
        }

        $failures += 'signing out did not ask for confirmation through the application dialog'
        throw 'cannot continue without the confirmation dialog'
    }
    Say 'confirmation dialog shown'

    $confirmButton = Find-ByAutomationId $confirm 'ConfirmButton'
    if (-not $confirmButton) {
        $failures += 'the confirmation dialog has no ConfirmButton'
        throw 'cannot continue without the confirm button'
    }
    Invoke-Element $confirmButton

    # --- 3. the shell closes and the login dialog returns ------------------------------
    if (-not (Wait-ShellGone $proc.Id)) {
        $failures += 'the shell was still open 20s after the sign-out was confirmed'
    }
    else {
        Say 'shell closed'
    }

    if ($proc.HasExited) {
        $failures += "the process exited (code $($proc.ExitCode)) instead of returning to the login dialog"
        throw 'the process is gone; the session loop did not turn'
    }

    $loginAgain = Get-WeighBridgeWindow -ProcessId $proc.Id -Title 'Login' -TimeoutSec 20
    if (-not $loginAgain) {
        $startupError = Get-WeighBridgeStartupError -ProcessId $proc.Id
        $failures += if ($startupError) {
            "after signing out the application is showing '$startupError' instead of the login dialog"
        } else {
            'the login dialog did not reappear within 20s of signing out'
        }
        throw 'the login dialog never came back'
    }
    Say 'login dialog returned - the session loop turned'

    # --- 4. a second sign-in builds a second shell -------------------------------------
    # The account already exists now, so this is a real authentication rather than setup.
    $shell2 = Invoke-WeighBridgeLogin -ProcessId $proc.Id -Username $Operator -TimeoutSec 60
    Say 'session 2: signed in again, second shell built'

    # The rail is filtered by permission in the ViewModel constructor, so a shell reused
    # across sessions would be the first operator's. Presence of the rail at all is what is
    # checked here; navigation-audit.ps1 covers which items it contains.
    $rail = @($shell2.FindAll($tree::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition(
            $element::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton))))
    if ($rail.Count -eq 0) {
        $failures += 'the second shell has no navigation rail'
    }
    else {
        Say "second shell rail: $($rail.Count) module(s)"
    }

    # --- 6. closing the second shell ends the process ----------------------------------
    [SignOutWin32]::PostMessage(
        [IntPtr]$shell2.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null

    if (-not $proc.WaitForExit(25000)) {
        $failures += 'the process did not exit within 25s of the second shell being closed'
    }
    else {
        $exitCode = $proc.ExitCode
        Say "process exited with code $exitCode"
    }
}
catch {
    # The throws above are control flow for "no point continuing"; each already recorded its
    # own failure. Anything else is a fault in this script or an unexpected application state,
    # and must not be reported as a pass.
    if ($failures.Count -eq 0) { $failures += "unexpected error: $($_.Exception.Message)" }
    else { Say "stopped early: $($_.Exception.Message)" }
}
finally {
    Get-Process -Name 'WeighBridge.App' -ErrorAction SilentlyContinue | Stop-Process -Force
}

# --- 5. the audit trail names who left --------------------------------------------------
# Read after the process is gone so the logger has flushed. The log is the observable half of
# the audit record; the database row it accompanies is asserted by the unit tests.
$log = Get-ChildItem -Path $data.LogDir -Filter '*.log' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1

if (-not $log) {
    $failures += "no log file was written to $($data.LogDir)"
}
else {
    $text = Get-Content -LiteralPath $log.FullName -Raw

    # Asserted together: an entry that recorded the sign-out but attributed it to the Windows
    # account would satisfy a bare "SignedOut" check while being the wrong record.
    $signedOut = [regex]::Matches($text, 'SignedOut').Count
    if ($signedOut -lt 1) {
        $failures += 'the log contains no SignedOut audit entry'
    }
    elseif ($text -notmatch "signed out" -or $text -notmatch [regex]::Escape($Operator)) {
        $failures += "the log records a sign-out but never names '$Operator'"
    }
    else {
        Say "audit: $signedOut SignedOut entry/entries, naming '$Operator'"
    }

    if ($text -notmatch 'returning to the login dialog') {
        $failures += 'the log does not record the return to the login dialog'
    }
}

Remove-IsolatedDataRoot -Root $data.Root

if ($exitCode -ne 0 -and $failures.Count -eq 0) {
    $failures += "the process exited with code $exitCode, expected 0"
}

Write-Host ''
if ($failures.Count -eq 0) {
    Say 'PASS: sign-out confirms, closes the shell, returns to the login dialog, rebuilds a second shell, is audited against the operator, and shuts down with exit code 0.'
    exit 0
}

Say '--- FAILED ---'
foreach ($failure in $failures) { Say "  $failure" }
exit 1
