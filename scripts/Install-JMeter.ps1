param([string]$Version = '5.6.3')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric JMeter release version.' }
if (!(Get-Command java -ErrorAction SilentlyContinue)) { throw 'Install Java 17 or 21 before configuring load tests.' }
$taskRoot = Split-Path $PSScriptRoot -Parent
$tools = Join-Path $taskRoot '.local/tools'
$destination = Join-Path $tools "apache-jmeter-$Version"
if (Test-Path -LiteralPath (Join-Path $destination 'bin/ApacheJMeter.jar')) { Write-Output "JMeter $Version is configured locally."; return }
New-Item -ItemType Directory -Path $tools -Force | Out-Null
$archive = Join-Path $tools "apache-jmeter-$Version.zip"
$url = "https://downloads.apache.org/jmeter/binaries/apache-jmeter-$Version.zip"
Invoke-WebRequest -Uri $url -OutFile $archive -MaximumRetryCount 2 -RetryIntervalSec 2
$checksum = (Invoke-WebRequest -Uri "$url.sha512").Content
if ($checksum -is [byte[]]) { $checksum = [Text.Encoding]::UTF8.GetString($checksum) }
$expected = [regex]::Match($checksum, '(?i)\b[0-9a-f]{128}\b').Value
if (!$expected -or (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expected.ToUpperInvariant()) { throw 'JMeter SHA-512 verification failed; archive was not executed.' }
Expand-Archive -LiteralPath $archive -DestinationPath $tools -Force
Write-Output "JMeter $Version downloaded from Apache and SHA-512 verified."
