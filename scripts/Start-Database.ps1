# 启动当前工作区拥有的MySQL实例，只监听回环地址。
param([string]$MySqlHome = $env:MYSQL_HOME, [int]$Port = 33316)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
# 优先复用初始化时保存的安装路径及端口。
$runtimePath = Join-Path $taskRoot '.local/runtime.json'
if (Test-Path -LiteralPath $runtimePath) {
    $runtime = Get-Content -LiteralPath $runtimePath -Raw | ConvertFrom-Json
    if (!$MySqlHome) { $MySqlHome = $runtime.MySqlHome }
    $Port = $runtime.Port
}
# 未初始化或路径无效时给出明确错误，避免误操作其他实例。
if (!$MySqlHome) { throw 'Pass -MySqlHome or set MYSQL_HOME to the MySQL 8.4 installation.' }
$mysqlServer = Join-Path $MySqlHome 'bin/mysqld.exe'
if (!(Test-Path -LiteralPath $mysqlServer)) { throw 'mysqld.exe was not found.' }
if (!(Test-Path -LiteralPath (Join-Path $taskRoot '.local/mysql-data/mysql'))) { throw 'Run Initialize-Local.ps1 first.' }
# 端口已监听时不再启动第二个实例。
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    Write-Output "A database is already listening on port $Port."
    return
}
# 使用ASCII目录联接，并验证目标确实是当前工作区。
$taskHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskRoot))).Substring(0,12)
$junctionParent = Join-Path $env:PUBLIC 'LibraryLending'
New-Item -ItemType Directory -Path $junctionParent -Force | Out-Null
$junction = Join-Path $junctionParent $taskHash
if (!(Test-Path -LiteralPath $junction)) { New-Item -ItemType Junction -Path $junction -Target $taskRoot | Out-Null }
$resolvedTarget = (Get-Item -LiteralPath $junction).Target
if ([IO.Path]::GetFullPath($resolvedTarget) -ne [IO.Path]::GetFullPath($taskRoot)) { throw 'Database junction points to a different workspace.' }
# 独立数据目录、回环端口与本地日志；后台启动不弹出窗口。
$mysqlArgs = @('--no-defaults', "--basedir=$($MySqlHome.Replace('\','/'))", "--datadir=$($junction.Replace('\','/'))/.local/mysql-data", "--port=$Port", '--bind-address=127.0.0.1', '--mysqlx=OFF', "--log-error=$($junction.Replace('\','/'))/.local/mysql.log")
$databaseProcess = Start-Process -FilePath $mysqlServer -ArgumentList $mysqlArgs -WindowStyle Hidden -PassThru
# 短间隔检查就绪状态，进程异常退出或超时都按失败报告。
for ($attempt = 0; $attempt -lt 80; $attempt++) {
    if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { Write-Output "MySQL is listening on 127.0.0.1:$Port."; return }
    if ($databaseProcess.HasExited) { throw 'MySQL exited. Inspect .local/mysql.log.' }
    Start-Sleep -Milliseconds 250
}
throw 'MySQL readiness timeout.'
