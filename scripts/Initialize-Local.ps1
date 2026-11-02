# 本地首次初始化；需要PowerShell 7和已安装的MySQL 8.4，生成的凭据只写入忽略目录。
# 参数指定MySQL安装位置和仅在本机监听的端口。
param([Parameter(Mandatory)][string]$MySqlHome, [int]$Port = 33316)
$ErrorActionPreference = 'Stop'
# 从脚本目录定位工作区，使从任意目录调用均使用同一配置。
$taskRoot = Split-Path $PSScriptRoot -Parent
$privateDirectory = Join-Path $taskRoot '.local'
New-Item -ItemType Directory -Path $privateDirectory -Force | Out-Null
$runtimePath = Join-Path $privateDirectory 'runtime.json'
# 已有配置时复用数据库，避免覆盖已有账号密码。
if (Test-Path -LiteralPath (Join-Path $privateDirectory 'test-settings.json')) {
    & (Join-Path $PSScriptRoot 'Start-Database.ps1') -MySqlHome $MySqlHome -Port $Port
    Write-Output 'Existing local settings retained.'
    return
}
if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) { throw 'Port is occupied. Choose a different -Port.' }
# 校验安装路径并记录本地运行配置。
$mysqlServer = Join-Path $MySqlHome 'bin/mysqld.exe'
$mysqlClient = Join-Path $MySqlHome 'bin/mysql.exe'
if (!(Test-Path -LiteralPath $mysqlServer)) { throw 'The selected directory does not contain bin/mysqld.exe.' }
@{ MySqlHome = [IO.Path]::GetFullPath($MySqlHome); Port = $Port } | ConvertTo-Json | Set-Content -LiteralPath $runtimePath -Encoding utf8
$dataDirectory = Join-Path $privateDirectory 'mysql-data'
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
# 使用受校验的ASCII目录联接处理Windows原生程序的非ASCII路径参数。
$taskHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskRoot))).Substring(0,12)
$junctionParent = Join-Path $env:PUBLIC 'LibraryLending'
New-Item -ItemType Directory -Path $junctionParent -Force | Out-Null
$junction = Join-Path $junctionParent $taskHash
if (!(Test-Path -LiteralPath $junction)) { New-Item -ItemType Junction -Path $junction -Target $taskRoot | Out-Null }
$resolvedTarget = (Get-Item -LiteralPath $junction).Target
if ([IO.Path]::GetFullPath($resolvedTarget) -ne [IO.Path]::GetFullPath($taskRoot)) { throw 'The junction points to another workspace.' }
# 只有尚未初始化的数据目录才执行初始化；失败立即终止。
if (!(Test-Path -LiteralPath (Join-Path $dataDirectory 'mysql'))) {
    & $mysqlServer --no-defaults --initialize-insecure "--basedir=$MySqlHome" "--datadir=$junction/.local/mysql-data" "--log-error=$junction/.local/mysql-init.log"
    if ($LASTEXITCODE -ne 0) { throw 'MySQL initialization failed. Inspect .local/mysql-init.log.' }
}
& (Join-Path $PSScriptRoot 'Start-Database.ps1') -MySqlHome $MySqlHome -Port $Port
# 每个账号生成独立随机密码，输出文件受Git忽略规则保护。
function New-LocalPassword { 'Local' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(20)) + '9' }
$devPassword = New-LocalPassword
$testPassword = New-LocalPassword
$rootPassword = New-LocalPassword
$demoPassword = New-LocalPassword
# 开发账号仅访问开发库；测试账号仅能创建和操作隔离测试库。
$sql = @"
CREATE DATABASE IF NOT EXISTS library_dev CHARACTER SET utf8mb4;
CREATE USER 'librarydev'@'localhost' IDENTIFIED BY '$devPassword';
GRANT ALL ON library_dev.* TO 'librarydev'@'localhost';
CREATE USER 'librarytests'@'localhost' IDENTIFIED BY '$testPassword';
GRANT ALL ON ``librarytest\_%``.* TO 'librarytests'@'localhost';
ALTER USER 'root'@'localhost' IDENTIFIED BY '$rootPassword';
"@
# 通过标准输入发送初始化SQL，避免把密码放进进程命令行。
$sql | & $mysqlClient --no-defaults --host=127.0.0.1 "--port=$Port" --user=root
if ($LASTEXITCODE -ne 0) { throw 'Database account provisioning failed.' }
# 私密连接、演示凭据与本地覆盖配置均不进入版本库。
@{ Connection = "Server=127.0.0.1;Port=$Port;User ID=librarytests;Password=$testPassword;SslMode=Preferred;AllowPublicKeyRetrieval=True"; Browser = 'edge' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $privateDirectory 'test-settings.json') -Encoding utf8
@{ ConnectionStrings = @{ Library = "Server=127.0.0.1;Port=$Port;Database=library_dev;User ID=librarydev;Password=$devPassword;SslMode=Preferred;AllowPublicKeyRetrieval=True" }; Database = @{ MigrateOnStartup = $true }; Seed = @{ Enabled = $true; AdminEmail = 'admin@library.example'; AdminPassword = $demoPassword; ReaderEmail = 'reader@library.example'; ReaderPassword = $demoPassword } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskRoot 'src/Library.Web/appsettings.Local.json') -Encoding utf8
"[client]`nuser=root`npassword=$rootPassword`nhost=127.0.0.1`nport=$Port" | Set-Content -LiteralPath (Join-Path $privateDirectory 'mysql-admin.cnf') -Encoding utf8
"# Local demo accounts`n`nAdmin: admin@library.example`nReader: reader@library.example`nPassword: $demoPassword`n" | Set-Content -LiteralPath (Join-Path $privateDirectory 'demo-accounts.md') -Encoding utf8
Write-Output 'Local settings prepared. Demo credentials are in .local/demo-accounts.md.'
