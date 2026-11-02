# 在指定回环端口运行Web应用，本地覆盖配置由初始化脚本生成。
param([int]$Port = 5180)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
# 固定应用根目录，退出或异常时恢复调用者工作目录。
Push-Location $taskRoot
try {
    # 确认本地数据库已启动，再启动应用并由配置决定迁移和种子初始化。
    & (Join-Path $PSScriptRoot 'Start-Database.ps1')
    dotnet run --project src/Library.Web --no-launch-profile -- --urls "http://localhost:$Port"
} finally { Pop-Location }
