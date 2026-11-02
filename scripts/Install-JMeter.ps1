# 下载指定JMeter版本到本地忽略目录，并校验Apache提供的SHA-512。
param([string]$Version = '5.6.3')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# 只允许数字版本号并确认Java可用，防止无效下载与启动。
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric JMeter release version.' }
if (!(Get-Command java -ErrorAction SilentlyContinue)) { throw 'Install Java 17 or 21 before configuring load tests.' }
# 已安装时幂等返回，测试工具不会提交到版本库。
$taskRoot = Split-Path $PSScriptRoot -Parent
$tools = Join-Path $taskRoot '.local/tools'
$destination = Join-Path $tools "apache-jmeter-$Version"
if (Test-Path -LiteralPath (Join-Path $destination 'bin/ApacheJMeter.jar')) { Write-Output "JMeter $Version is configured locally."; return }
New-Item -ItemType Directory -Path $tools -Force | Out-Null
# 从Apache下载二进制包和校验值。
$archive = Join-Path $tools "apache-jmeter-$Version.zip"
$url = "https://downloads.apache.org/jmeter/binaries/apache-jmeter-$Version.zip"
Invoke-WebRequest -Uri $url -OutFile $archive -MaximumRetryCount 2 -RetryIntervalSec 2
$checksum = (Invoke-WebRequest -Uri "$url.sha512").Content
if ($checksum -is [byte[]]) { $checksum = [Text.Encoding]::UTF8.GetString($checksum) }
# 完整校验通过后才解压，校验失败保留诊断并终止。
$expected = [regex]::Match($checksum, '(?i)\b[0-9a-f]{128}\b').Value
if (!$expected -or (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expected.ToUpperInvariant()) { throw 'JMeter SHA-512 verification failed; archive was not executed.' }
Expand-Archive -LiteralPath $archive -DestinationPath $tools -Force
Write-Output "JMeter $Version downloaded from Apache and SHA-512 verified."
