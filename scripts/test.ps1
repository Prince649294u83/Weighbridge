param()

$ErrorActionPreference = "Stop"

Set-Location "$PSScriptRoot\.."

dotnet test --no-restore
