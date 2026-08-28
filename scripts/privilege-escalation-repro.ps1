# Reproduction for the user-management authorisation defect.
#
# Permissions.UsersManage is defined and granted to Administrator alone, but nothing
# consults it: AdministrationViewModel injects IPermissionService and never calls it,
# writes through IRepository<User> instead of CommandExecutor (which is the only place
# Authorize() runs), and the shell's navigation list is a fixed array with no permission
# filter.
#
# The claim under test: a ReadOnly operator can create an Administrator account.
#
# Exit 0 = the escalation was BLOCKED (defect absent or fixed).
# Exit 1 = the escalation SUCCEEDED (defect present).
# Exit 2 = inconclusive - the script could not complete the attempt.
#
# Inverted deliberately. A repro that exits 0 while reproducing the bug is exactly the
# false-positive shape this audit exists to remove.

$ErrorActionPreference = 'Stop'

# Isolated data root: this run must never touch the operator's live database. It also means the
# probe accounts below live in a directory that is deleted when the run ends, rather than being
# left in the operator's users grid.
. (Join-Path $PSScriptRoot 'isolated-data-root.ps1')
$data = New-IsolatedDataRoot -Label 'privilege-escalation-repro'
$projectRoot = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $projectRoot 'src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe'
$logFile = $data.LogFile

# Prefixed so a leftover row is unmistakable in the users grid if a run dies midway.
$probeUser = 'audit_readonly'
$escalatedUser = 'audit_escalated_admin'
# Generated per run rather than written down: a literal here reads as a credential even though
# it only ever unlocks an account this script created in a directory it is about to delete.
function New-ProbePassword {
    return -join ((1..16) | ForEach-Object { [char](Get-Random -InputObject (@(48..57) + @(65..90) + @(97..122))) })
}
$probePassword = New-ProbePassword

function Say($text) { Write-Host "[escalation] $text" }

if (-not (Test-Path $exe)) { Write-Error "exe not found: $exe" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
# Select-Role sends a keystroke through SendKeys, which lives here. Without this line the run
# ends INCONCLUSIVE on a missing type rather than reporting anything about authorisation.
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'login-helper.ps1')

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Click32 {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        System.Threading.Thread.Sleep(40);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
    }
}
'@

$UIA = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]
$Ctrl = [System.Windows.Automation.ControlType]
$baseline = if (Test-Path $logFile) { (Get-Item $logFile).Length } else { 0 }

function New-Condition($property, $value) {
    return New-Object System.Windows.Automation.PropertyCondition($property, $value)
}

function Find-ByNameAndType($scope, $name, $type) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Condition $UIA::NameProperty $name),
        (New-Condition $UIA::ControlTypeProperty $type))
    return $scope.FindFirst($Tree::Descendants, $cond)
}

function Invoke-Named($scope, $name, $type) {
    $element = Find-ByNameAndType $scope $name $type
    if (-not $element) { throw "no $($type.ProgrammaticName) named '$name'" }
    if (-not $element.Current.IsEnabled) { throw "the control named '$name' is disabled" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 500
}

function Get-RunLog {
    if (-not (Test-Path $logFile)) { return '' }
    $stream = [System.IO.File]::Open($logFile, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($baseline, 'Begin') | Out-Null
        return (New-Object System.IO.StreamReader($stream)).ReadToEnd()
    } finally { $stream.Dispose() }
}

# Navigation is confirmed by a log count delta, not by the radio button's checked state:
# the command is bound to Click, so ticking the button proves nothing about the content.
function Open-Administration($shell) {
    $item = Find-ByNameAndType $shell 'Administration' $Ctrl::RadioButton
    if (-not $item) { return $false }

    [WeighBridgeFocus]::SetForegroundWindow([IntPtr]$shell.Current.NativeWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 400

    foreach ($attempt in 1, 2) {
        $before = ([regex]::Matches((Get-RunLog), 'Navigated to AdministrationViewModel')).Count
        try {
            $item.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        } catch {
            $point = $item.GetClickablePoint()
            [Click32]::Click([int]$point.X, [int]$point.Y)
        }
        $deadline = (Get-Date).AddSeconds(6)
        while ((Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
            if (([regex]::Matches((Get-RunLog), 'Navigated to AdministrationViewModel')).Count -gt $before) {
                return $true
            }
        }
        Say "  retrying the click on Administration (attempt $attempt produced no navigation)"
    }
    return $false
}

# AdministrationView sets no AutomationProperties.Name on its inputs, so the three text
# boxes cannot be addressed by label. The 'Account is Active' check box is unique and
# shares their UIA parent, so it anchors the panel; the boxes are then taken in tree
# order: Username, Display Name, Password.
function Get-FormFields($shell) {
    $anchor = Find-ByNameAndType $shell 'Account is Active' $Ctrl::CheckBox
    if (-not $anchor) { throw 'the user editor panel is not open (no "Account is Active" check box)' }

    $panel = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($anchor)
    if (-not $panel) { throw 'could not reach the editor panel from the check box' }

    $boxes = $panel.FindAll($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::Edit))
    if ($boxes.Count -lt 3) { throw "the editor panel exposes $($boxes.Count) text boxes, expected 3" }

    return @{
        Username    = $boxes[0]
        DisplayName = $boxes[1]
        Password    = $boxes[2]
        Role        = $panel.FindFirst($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::ComboBox))
    }
}

function Set-Value($element, $value) {
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($value)
    Start-Sleep -Milliseconds 150
}

function Select-Role($combo, $roleName) {
    if (-not $combo) { throw 'the editor panel has no role combo box' }

    # The drop-down is hosted in its own popup window, so its items are not reachable as
    # UIA descendants of the combo. WPF selects on first-letter keypress with the combo
    # closed, and the four shipped roles have four distinct initials.
    $combo.SetFocus()
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait($roleName.Substring(0, 1))
    Start-Sleep -Milliseconds 400

    # Read back rather than assume: a keystroke that selected the wrong role would
    # otherwise create the probe user at the wrong privilege level and invalidate the run.
    # The item's UIA name is Role.ToString() ("ReadOnly (2 permission(s))"), not the
    # DisplayMemberPath text, so this matches on the leading role name.
    $selection = $combo.GetCurrentPattern(
        [System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($selection.Count -lt 1) { throw "the role combo box selected nothing for '$roleName'" }
    if ($selection[0].Current.Name -notlike "$roleName*") {
        throw "the role combo box selected '$($selection[0].Current.Name)', not '$roleName'"
    }
}

# Reads the users DataGrid rather than all screen text: the form fields echo whatever was
# just typed, so matching on screen text would report a filled-in form as a saved user.
function Get-GridUsernames($shell) {
    $grid = $shell.FindFirst($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::DataGrid))
    if (-not $grid) { return @() }
    $cells = $grid.FindAll($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::Text))
    $names = @()
    foreach ($cell in $cells) { $names += $cell.Current.Name }
    return $names
}

function New-User($shell, $username, $displayName, $password, $role) {
    Invoke-Named $shell '+ Add User' $Ctrl::Button
    $fields = Get-FormFields $shell
    Set-Value $fields.Username $username
    Set-Value $fields.DisplayName $displayName
    Set-Value $fields.Password $password
    Select-Role $fields.Role $role

    # The save button's label is data-bound to FormSaveLabel, which is "Create" for a new
    # user and "Save Changes" when editing an existing one.
    $saved = $false
    foreach ($label in 'Create', 'Save Changes') {
        if (Find-ByNameAndType $shell $label $Ctrl::Button) {
            Invoke-Named $shell $label $Ctrl::Button
            $saved = $true
            break
        }
    }
    if (-not $saved) { throw 'the editor panel has no save button' }

    Start-Sleep -Seconds 1
    $deadline = (Get-Date).AddSeconds(6)
    while ((Get-Date) -lt $deadline) {
        if ((Get-GridUsernames $shell) -contains $username) { return $true }
        Start-Sleep -Milliseconds 300
    }

    # The save did not take. Nothing is logged on this path and the view model swallows
    # the reason, so the screen is the only place the cause is visible.
    Say '  --- save did not take; screen contents follow ---'
    $texts = $shell.FindAll($Tree::Descendants, (New-Condition $UIA::ControlTypeProperty $Ctrl::Text))
    foreach ($t in $texts) { if ($t.Current.Name.Trim()) { Say "    text: $($t.Current.Name)" } }
    $fields = Get-FormFields $shell
    foreach ($name in 'Username', 'DisplayName', 'Password') {
        $value = $fields[$name].GetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
        Say "    field ${name}: '$value'"
    }
    Say '  --- end screen contents ---'
    return $false
}

function Stop-App($shell, $process) {
    try {
        $shell.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    } catch { }
    if (-not $process.WaitForExit(15000)) { $process.Kill() }
}

$proc = $null
$escalated = $false
try {
    # --- Phase 1: as the seeded administrator, create a ReadOnly account ------
    Say 'Phase 1: creating a ReadOnly account as the seeded administrator'
    $proc = Start-Process -FilePath $exe -PassThru
    $shell = Invoke-WeighBridgeLogin -ProcessId $proc.Id -TimeoutSec 45

    if (-not (Open-Administration $shell)) { throw 'admin: Administration did not open' }

    # No reuse branch: the data root is new every run, so an account of this name already
    # existing would mean the isolation is not in effect - and its password would not be the
    # one generated above, so phase 2 would fail on the login rather than on a privilege.
    if ((Get-GridUsernames $shell) -contains $probeUser) {
        throw "admin: '$probeUser' already exists in a data root that should be empty - isolation is not in effect"
    }
    if (New-User $shell $probeUser 'Audit Read Only Probe' $probePassword 'ReadOnly') {
        Say "  created '$probeUser' with role ReadOnly"
    } else {
        throw "admin: '$probeUser' was not created - it is not listed in the users grid"
    }

    Stop-App $shell $proc

    # --- Phase 2: log in as that ReadOnly account and try to manage users ----
    Say "Phase 2: logging in as '$probeUser' (role ReadOnly)"
    $proc = Start-Process -FilePath $exe -PassThru
    $shell = Invoke-WeighBridgeLogin -ProcessId $proc.Id -Username $probeUser -Password $probePassword -TimeoutSec 45

    if (-not (Find-ByNameAndType $shell 'Administration' $Ctrl::RadioButton)) {
        Say '  BLOCKED: Administration is not offered in the navigation rail to a ReadOnly operator'
    } elseif (-not (Open-Administration $shell)) {
        Say '  BLOCKED: the Administration navigation item is present but does not navigate'
    } else {
        Say '  a ReadOnly operator reached the Administration module'
        $addButton = Find-ByNameAndType $shell '+ Add User' $Ctrl::Button
        if (-not $addButton) {
            Say '  BLOCKED: the module opened but offers no "+ Add User" control'
        } elseif (-not $addButton.Current.IsEnabled) {
            Say '  BLOCKED: "+ Add User" is present but disabled'
        } else {
            # An enabled button is not proof. The create is completed and the grid checked.
            $escalated = New-User $shell $escalatedUser 'Created By A ReadOnly User' (New-ProbePassword) 'Administrator'
            if ($escalated) {
                Say '  ESCALATED: a ReadOnly operator created an Administrator account'
            } else {
                Say '  BLOCKED: the save was refused'
            }
        }
    }

    Stop-App $shell $proc

    Write-Host ''
    if ($escalated) {
        Write-Host '[escalation] REPRODUCED: user management enforces no authorisation.' -ForegroundColor Red
        Write-Host "[escalation] Probe accounts were created in the temporary root: $probeUser, $escalatedUser" -ForegroundColor Yellow
        exit 1
    }

    Write-Host '[escalation] Escalation blocked - UsersManage is enforced.' -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "[escalation] INCONCLUSIVE: $($_.Exception.Message)" -ForegroundColor Yellow
    exit 2
}
finally {
    if ($proc -and -not $proc.HasExited) {
        $proc.Kill()
        Say "killed leftover process $($proc.Id)"
    }
    Remove-IsolatedDataRoot -Root $data.Root
}
