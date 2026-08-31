# Shared authentication step for the runtime smoke scripts.
#
# Prompt 10 put a blocking modal login dialog in front of the shell. Every runtime
# script therefore has to authenticate before it can assert on anything, and every
# script has to identify the shell positively - a script that accepts "any window
# belonging to the process" passes on a startup-failure dialog, which is how a broken
# startup went unnoticed.
#
# Dot-source this after the UIAutomation assemblies are loaded:
#   . "$PSScriptRoot\login-helper.ps1"
#   $shell = Invoke-WeighBridgeLogin -ProcessId $processId

Add-Type -AssemblyName System.Windows.Forms

if (-not ('WeighBridgeFocus' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WeighBridgeFocus {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
'@
}

# The shell is matched on an explicit AutomationId, not its title: the title is
# data-bound, and UIA reports ClassName 'Window' for every WPF window including dialogs.
$script:ShellAutomationId = 'ShellWindow'
$script:LoginWindowTitle = 'Login'

# The same dialog in its first-run mode, where it appoints the administrator instead of
# authenticating one. The application seeds no account, so a database with no users shows
# this and nothing else. The title is the only thing that distinguishes the two modes from
# outside the process, and typing a sign-in into a setup form and reporting a successful
# login is exactly the kind of false pass this audit exists to stop.
$script:SetupWindowTitle = 'Administrator setup'

# Generated once per PowerShell session rather than written down. Every script here appoints
# its own administrator into a throwaway database, so the value only has to be the same for
# the setup call and the sign-ins that follow it inside one run - and a literal default meant
# every run on every machine appointed an administrator with the same known password.
$script:GeneratedPassword = 'Wb-' + (-join ((1..20) | ForEach-Object {
    [char](Get-Random -InputObject (@(48..57) + @(65..90) + @(97..122)))
}))

<#
.SYNOPSIS
Returns a top-level window of the process, matched by title or by automation id.
#>
function Get-WeighBridgeWindow {
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [string]$Title,
        [string]$AutomationId,
        [int]$TimeoutSec = 30
    )

    if (-not $Title -and -not $AutomationId) {
        throw 'Get-WeighBridgeWindow needs either -Title or -AutomationId.'
    }

    $element = [System.Windows.Automation.AutomationElement]
    $deadline = (Get-Date).AddSeconds($TimeoutSec)

    while ($true) {
        try {
            $windows = $element::RootElement.FindAll(
                [System.Windows.Automation.TreeScope]::Children,
                (New-Object System.Windows.Automation.PropertyCondition(
                    $element::ProcessIdProperty, $ProcessId)))

            foreach ($window in $windows) {
                if ($Title -and $window.Current.Name -ne $Title) { continue }
                if ($AutomationId -and $window.Current.AutomationId -ne $AutomationId) { continue }
                return $window
            }
        }
        catch {
            # A window closing mid-enumeration throws; the next poll sees a stable tree.
        }

        if ((Get-Date) -ge $deadline) { return $null }
        Start-Sleep -Milliseconds 250
    }
}

<#
.SYNOPSIS
Names the failure dialog if the application is showing one.
.DESCRIPTION
The four titles are every way the application reports a fault it survived:
App.ReportStartupFailureAsync, App.OnStartup's database check, MainWindow's initialisation
handler, and the DispatcherUnhandledException handler - which marks exceptions handled, so
without this check an unhandled UI-thread exception leaves no trace in an automated run.

Any dialog title added to the application has to be added here too. A title this list does
not know is a failure every script using it will report as "the window never appeared",
which sends the next person looking in the wrong place.
#>
function Get-WeighBridgeStartupError {
    param([Parameter(Mandatory)][int]$ProcessId)

    foreach ($title in 'WeighBridge could not start', 'WeighBridge cannot open its database',
                       'Startup was not completed', 'Something went wrong') {
        $dialog = Get-WeighBridgeWindow -ProcessId $ProcessId -Title $title -TimeoutSec 0
        if ($dialog) { return $title }
    }

    return $null
}

<#
.SYNOPSIS
Authenticates through the real login dialog and returns the shell window.
.DESCRIPTION
Handles both modes of the dialog. On a database with no accounts the application shows its
first-run setup form, and this appoints the administrator with the credential passed in;
after that, and on any database that already has accounts, it signs in.

Throws if neither form appears, if a field is missing, if startup reports a failure, or if
the shell does not open - so a caller that ignores the return value still fails the run.

The credential is generated per session rather than written down, because it is created by
this script into a throwaway test database and nothing in the product knows it. A database
created before password hashing was salted still holds accounts with their old passwords, so
running against one means passing -Username and -Password for it.
#>
function Invoke-WeighBridgeLogin {
    param(
        [Parameter(Mandatory)][int]$ProcessId,
        [string]$Username = 'admin',
        [string]$Password = $script:GeneratedPassword,
        [int]$TimeoutSec = 30
    )

    $element = [System.Windows.Automation.AutomationElement]
    $tree = [System.Windows.Automation.TreeScope]

    # Setup is checked first: a database with no accounts can only show that form, and
    # asking for 'Login' first would burn the whole timeout before ever looking.
    $login = $null
    $mode = $null
    $deadline = (Get-Date).AddSeconds($TimeoutSec)

    while ($true) {
        foreach ($title in $script:SetupWindowTitle, $script:LoginWindowTitle) {
            $candidate = Get-WeighBridgeWindow -ProcessId $ProcessId -Title $title -TimeoutSec 0
            if ($candidate) { $login = $candidate; $mode = $title; break }
        }

        if ($login) { break }

        if ((Get-Date) -ge $deadline) {
            $startupError = Get-WeighBridgeStartupError -ProcessId $ProcessId
            if ($startupError) {
                throw "The application is showing '$startupError' instead of a login dialog."
            }
            throw "Neither the login dialog nor first-run setup appeared within ${TimeoutSec}s."
        }

        Start-Sleep -Milliseconds 250
    }

    [WeighBridgeFocus]::SetForegroundWindow([IntPtr]$login.Current.NativeWindowHandle) | Out-Null

    # WPF sets a window's title before it has realised the window's content, so the poll
    # above can return the dialog while its fields are still absent from the UIA tree.
    #
    # Polling that one captured element is not enough: WPF's automation peers cache their
    # children, and the peer our own early query created can keep answering with an empty
    # tree indefinitely. One cycle in ten failed that way with the application sitting
    # healthy at the dialog, logging nothing, waiting to be typed into. So each pass
    # re-acquires the window from the desktop root and re-asserts foreground, which makes
    # the provider rebuild the tree instead of confirming a stale answer.
    #
    # The deadline is a deadline, not a sleep, and the tree is dumped when it expires:
    # an intermittent "field not found" with no other evidence leaves the next
    # reproduction exactly as uninformative as the last one.
    function Find-Field($automationId) {
        $deadline = (Get-Date).AddSeconds(20)
        $window = $login

        while ($true) {
            if ($window) {
                $found = $window.FindFirst($tree::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition(
                        $element::AutomationIdProperty, $automationId)))
                if ($found) { return $found }

                [WeighBridgeFocus]::SetForegroundWindow(
                    [IntPtr]$window.Current.NativeWindowHandle) | Out-Null
            }

            if ((Get-Date) -ge $deadline) {
                $seen = if ($window) {
                    @($window.FindAll($tree::Descendants,
                        [System.Windows.Automation.Condition]::TrueCondition) |
                        Select-Object -First 25 |
                        ForEach-Object { "$($_.Current.ControlType.ProgrammaticName.Split('.')[-1]):$($_.Current.AutomationId)" })
                } else { @('the window is gone') }

                throw ("The '$mode' dialog has no element with AutomationId '$automationId' after 20s. " +
                    "UIA sees: $(if ($seen.Count) { $seen -join ', ' } else { 'nothing at all' })")
            }

            Start-Sleep -Milliseconds 150
            $window = Get-WeighBridgeWindow -ProcessId $ProcessId -Title $mode -TimeoutSec 0
        }
    }

    $usernameBox = Find-Field 'UsernameBox'
    $usernameBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Username)

    function Send-SafeKeys($targetElement, $text) {
        $targetElement.SetFocus()
        Start-Sleep -Milliseconds 60
        try {
            [System.Windows.Forms.SendKeys]::SendWait($text)
        }
        catch {
            $hWnd = [IntPtr]$login.Current.NativeWindowHandle
            foreach ($c in $text.ToCharArray()) {
                [WeighBridgeFocus]::PostMessage($hWnd, 0x0102, [IntPtr][int]$c, [IntPtr]::Zero) | Out-Null
                Start-Sleep -Milliseconds 15
            }
        }
    }

    $pwField = Find-Field 'PasswordBox'
    Send-SafeKeys $pwField '^a{DEL}'
    Start-Sleep -Milliseconds 80
    Send-SafeKeys $pwField $Password

    if ($mode -eq $script:SetupWindowTitle) {
        # Only present in setup mode, and the dialog refuses to submit without it.
        $confirmField = Find-Field 'ConfirmPasswordBox'
        Send-SafeKeys $confirmField '^a{DEL}'
        Start-Sleep -Milliseconds 80
        Send-SafeKeys $confirmField $Password
    }

    (Find-Field 'LoginButton').GetCurrentPattern(
        [System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $shell = Get-WeighBridgeWindow -ProcessId $ProcessId -AutomationId $script:ShellAutomationId -TimeoutSec $TimeoutSec
    if (-not $shell) {
        $startupError = Get-WeighBridgeStartupError -ProcessId $ProcessId
        if ($startupError) {
            throw "Startup failed after submitting the '$mode' dialog as '$Username': the application is showing '$startupError'."
        }

        # The dialog rejecting the credential looks identical to a shell that never opened
        # unless its own error banner is read back, and "shell did not appear" would send
        # the next person looking in the wrong place entirely.
        $stillOpen = Get-WeighBridgeWindow -ProcessId $ProcessId -Title $mode -TimeoutSec 0
        if ($stillOpen) {
            $banner = @($stillOpen.FindAll($tree::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    $element::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) |
                ForEach-Object { $_.Current.Name } |
                Where-Object { $_ })

            throw ("The '$mode' dialog did not accept '$Username'. It is still open and says: " +
                ($banner -join ' | '))
        }

        throw "Shell window did not open within ${TimeoutSec}s of submitting the '$mode' dialog as '$Username'."
    }

    # The shell can be up while a module inside it failed to load, which is a failure
    # the caller must not mistake for success.
    $startupError = Get-WeighBridgeStartupError -ProcessId $ProcessId
    if ($startupError) {
        throw "The shell opened but the application is showing '$startupError'."
    }

    return $shell
}
