param()

$ErrorActionPreference = "Stop"

Set-Location "$PSScriptRoot\.."

dotnet restore
dotnet build --no-restore
