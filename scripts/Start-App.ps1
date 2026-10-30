param([int]$Port = 5180)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    & (Join-Path $PSScriptRoot 'Start-Database.ps1')
    dotnet run --project src/Library.Web --no-launch-profile -- --urls "http://localhost:$Port"
} finally { Pop-Location }
