# Run with PowerShell 7 and an installed MySQL 8.4 distribution.
param([Parameter(Mandatory)][string]$MySqlHome, [int]$Port = 33316)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$privateDirectory = Join-Path $taskRoot '.local'
New-Item -ItemType Directory -Path $privateDirectory -Force | Out-Null
$runtimePath = Join-Path $privateDirectory 'runtime.json'
if (Test-Path -LiteralPath (Join-Path $privateDirectory 'test-settings.json')) {
    & (Join-Path $PSScriptRoot 'Start-Database.ps1') -MySqlHome $MySqlHome -Port $Port
    Write-Output 'Existing local settings retained.'
    return
}
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { throw 'Port is occupied. Choose a different -Port.' }
$mysqlServer = Join-Path $MySqlHome 'bin/mysqld.exe'
$mysqlClient = Join-Path $MySqlHome 'bin/mysql.exe'
if (!(Test-Path -LiteralPath $mysqlServer)) { throw 'The selected directory does not contain bin/mysqld.exe.' }
@{ MySqlHome = [IO.Path]::GetFullPath($MySqlHome); Port = $Port } | ConvertTo-Json | Set-Content -LiteralPath $runtimePath -Encoding utf8
$dataDirectory = Join-Path $privateDirectory 'mysql-data'
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
$taskHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskRoot))).Substring(0,12)
$junctionParent = Join-Path $env:PUBLIC 'LibraryLending'
New-Item -ItemType Directory -Path $junctionParent -Force | Out-Null
$junction = Join-Path $junctionParent $taskHash
if (!(Test-Path -LiteralPath $junction)) { New-Item -ItemType Junction -Path $junction -Target $taskRoot | Out-Null }
$resolvedTarget = (Get-Item -LiteralPath $junction).Target
if ([IO.Path]::GetFullPath($resolvedTarget) -ne [IO.Path]::GetFullPath($taskRoot)) { throw 'The junction points to another workspace.' }
if (!(Test-Path -LiteralPath (Join-Path $dataDirectory 'mysql'))) {
    & $mysqlServer --no-defaults --initialize-insecure "--basedir=$MySqlHome" "--datadir=$junction/.local/mysql-data" "--log-error=$junction/.local/mysql-init.log"
    if ($LASTEXITCODE -ne 0) { throw 'MySQL initialization failed. Inspect .local/mysql-init.log.' }
}
& (Join-Path $PSScriptRoot 'Start-Database.ps1') -MySqlHome $MySqlHome -Port $Port
function New-LocalPassword { 'Local' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(20)) + '9' }
$devPassword = New-LocalPassword
$testPassword = New-LocalPassword
$rootPassword = New-LocalPassword
$demoPassword = New-LocalPassword
$sql = @"
CREATE DATABASE IF NOT EXISTS library_dev CHARACTER SET utf8mb4;
CREATE USER 'librarydev'@'localhost' IDENTIFIED BY '$devPassword';
GRANT ALL ON library_dev.* TO 'librarydev'@'localhost';
CREATE USER 'librarytests'@'localhost' IDENTIFIED BY '$testPassword';
GRANT ALL ON ``librarytest\_%``.* TO 'librarytests'@'localhost';
ALTER USER 'root'@'localhost' IDENTIFIED BY '$rootPassword';
"@
$sql | & $mysqlClient --no-defaults --host=127.0.0.1 "--port=$Port" --user=root
if ($LASTEXITCODE -ne 0) { throw 'Database account provisioning failed.' }
@{ Connection = "Server=127.0.0.1;Port=$Port;User ID=librarytests;Password=$testPassword;SslMode=Preferred;AllowPublicKeyRetrieval=True"; Browser = 'edge' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $privateDirectory 'test-settings.json') -Encoding utf8
@{ ConnectionStrings = @{ Library = "Server=127.0.0.1;Port=$Port;Database=library_dev;User ID=librarydev;Password=$devPassword;SslMode=Preferred;AllowPublicKeyRetrieval=True" }; Database = @{ MigrateOnStartup = $true }; Seed = @{ Enabled = $true; AdminEmail = 'admin@library.example'; AdminPassword = $demoPassword; ReaderEmail = 'reader@library.example'; ReaderPassword = $demoPassword } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRoot 'src/Library.Web/appsettings.Local.json') -Encoding utf8
"[client]`nuser=root`npassword=$rootPassword`nhost=127.0.0.1`nport=$Port" | Set-Content -LiteralPath (Join-Path $privateDirectory 'mysql-admin.cnf') -Encoding utf8
"# Local demo accounts`n`nAdmin: admin@library.example`nReader: reader@library.example`nPassword: $demoPassword`n" | Set-Content -LiteralPath (Join-Path $privateDirectory 'demo-accounts.md') -Encoding utf8
Write-Output 'Local settings prepared. Demo credentials are in .local/demo-accounts.md.'
