$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$edgeBinary = Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe'
if (!(Test-Path -LiteralPath $edgeBinary)) { $edgeBinary = Join-Path $env:ProgramFiles 'Microsoft/Edge/Application/msedge.exe' }
if (!(Test-Path -LiteralPath $edgeBinary)) { throw 'Install Microsoft Edge first.' }
$edgeVersion = (Get-Item -LiteralPath $edgeBinary).VersionInfo.ProductVersion
$driverDirectory = Join-Path $taskRoot '.local/webdriver'
New-Item -ItemType Directory -Path $driverDirectory -Force | Out-Null
$archivePath = Join-Path $driverDirectory 'edge.zip'
Invoke-WebRequest -Uri "https://msedgedriver.microsoft.com/$edgeVersion/edgedriver_win64.zip" -OutFile $archivePath
Expand-Archive -LiteralPath $archivePath -DestinationPath $driverDirectory -Force
& (Join-Path $driverDirectory 'msedgedriver.exe') --version
if ($LASTEXITCODE -ne 0) { throw 'Driver installation could not be verified.' }
