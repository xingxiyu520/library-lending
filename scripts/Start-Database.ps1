param([string]$MySqlHome = $env:MYSQL_HOME, [int]$Port = 33316)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$runtimePath = Join-Path $taskRoot '.local/runtime.json'
if (Test-Path -LiteralPath $runtimePath) {
    $runtime = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
    if (!$MySqlHome) { $MySqlHome = $runtime.MySqlHome }
    $Port = $runtime.Port
}
if (!$MySqlHome) { throw 'Pass -MySqlHome or set MYSQL_HOME to the MySQL 8.4 installation.' }
$mysqlServer = Join-Path $MySqlHome 'bin/mysqld.exe'
if (!(Test-Path -LiteralPath $mysqlServer)) { throw 'mysqld.exe was not found.' }
if (!(Test-Path -LiteralPath (Join-Path $taskRoot '.local/mysql-data/mysql'))) { throw 'Run Initialize-Local.ps1 first.' }
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    Write-Output "A database is already listening on port $Port."
    return
}
# Use an ASCII junction because the Windows server can truncate non-ASCII arguments.
$taskHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskRoot))).Substring(0,12)
$junctionParent = Join-Path $env:PUBLIC 'LibraryLending'
New-Item -ItemType Directory -Path $junctionParent -Force | Out-Null
$junction = Join-Path $junctionParent $taskHash
if (!(Test-Path -LiteralPath $junction)) { New-Item -ItemType Junction -Path $junction -Target $taskRoot | Out-Null }
$resolvedTarget = (Get-Item -LiteralPath $junction).Target
if ([IO.Path]::GetFullPath($resolvedTarget) -ne [IO.Path]::GetFullPath($taskRoot)) { throw 'Database junction points to a different workspace.' }
$mysqlArgs = @('--no-defaults', "--basedir=$($MySqlHome.Replace('\','/'))", "--datadir=$($junction.Replace('\','/'))/.local/mysql-data", "--port=$Port", '--bind-address=127.0.0.1', '--mysqlx=OFF', "--log-error=$($junction.Replace('\','/'))/.local/mysql.log")
$databaseProcess = Start-Process -FilePath $mysqlServer -ArgumentList $mysqlArgs -WindowStyle Hidden -PassThru
for ($attempt = 0; $attempt -lt 80; $attempt++) {
    if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { Write-Output "MySQL is listening on 127.0.0.1:$Port."; return }
    if ($databaseProcess.HasExited) { throw 'MySQL exited. Inspect .local/mysql.log.' }
    Start-Sleep -Milliseconds 250
}
throw 'MySQL readiness timeout.'
