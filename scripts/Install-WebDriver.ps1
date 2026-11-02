# 为已安装的Microsoft Edge下载同版本WebDriver，仅保存在本地。
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
# 兼容两种常见安装位置，并从实际浏览器读取完整版本号。
$edgeBinary = Join-Path ${env:ProgramFiles(x86)} 'Microsoft/Edge/Application/msedge.exe'
if (!(Test-Path -LiteralPath $edgeBinary)) { $edgeBinary = Join-Path $env:ProgramFiles 'Microsoft/Edge/Application/msedge.exe' }
if (!(Test-Path -LiteralPath $edgeBinary)) { throw 'Install Microsoft Edge first.' }
$edgeVersion = (Get-Item -LiteralPath $edgeBinary).VersionInfo.ProductVersion
# 驱动按当前浏览器版本下载，避免版本不匹配导致浏览器测试失败。
$driverDirectory = Join-Path $taskRoot '.local/webdriver'
New-Item -ItemType Directory -Path $driverDirectory -Force | Out-Null
$archivePath = Join-Path $driverDirectory 'edge.zip'
Invoke-WebRequest -Uri "https://msedgedriver.microsoft.com/$edgeVersion/edgedriver_win64.zip" -OutFile $archivePath
Expand-Archive -LiteralPath $archivePath -DestinationPath $driverDirectory -Force
# 运行版本查询确认解压后的驱动可执行。
& (Join-Path $driverDirectory 'msedgedriver.exe') --version
if ($LASTEXITCODE -ne 0) { throw 'Driver installation could not be verified.' }
