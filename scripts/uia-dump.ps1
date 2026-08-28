# Dumps the UI Automation tree of the running app, so the smoke test can target
# real control names instead of guesses. Diagnostic helper, not part of CI.

$ErrorActionPreference = 'Stop'
$exe = Join-Path (Split-Path -Parent $PSScriptRoot) "src\WeighBridge.App\bin\Debug\net8.0-windows\WeighBridge.App.exe"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$process = Start-Process -FilePath $exe -PassThru
$deadline = (Get-Date).AddSeconds(45)
$root = $null
while ((Get-Date) -lt $deadline -and -not $root) {
    Start-Sleep -Milliseconds 500
    try {
        $root = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            (New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)))
    } catch { $root = $null }
}
if (-not $root) { Stop-Process -Id $process.Id -Force; Write-Error 'no window' }

Start-Sleep -Seconds 2

$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

function Dump($element, $depth) {
    if ($depth -gt 12) { return }
    $pad = '  ' * $depth
    $c = $element.Current
    Write-Host ("{0}{1} name='{2}' id='{3}'" -f $pad, $c.ControlType.ProgrammaticName, $c.Name, $c.AutomationId)
    $child = $walker.GetFirstChild($element)
    while ($child) {
        Dump $child ($depth + 1)
        $child = $walker.GetNextSibling($child)
    }
}

Dump $root 0

Stop-Process -Id $process.Id -Force
