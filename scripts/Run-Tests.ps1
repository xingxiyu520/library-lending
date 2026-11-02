# 统一构建并运行所有MSTest测试，包含真实数据库、浏览器及JMeter场景。
param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
# 固定解决方案目录，确保runsettings与结果目录一致。
Push-Location $taskRoot
try {
    # 先构建；编译失败时不运行旧的测试程序集。
    dotnet build LibraryLending.sln --configuration $Configuration --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    # 使用同一测试程序集，Visual Studio也通过MSTest适配器发现这些用例。
    $runner = Join-Path $taskRoot "tests/Library.Tests/bin/$Configuration/net8.0/Library.Tests.exe"
    # 最少数量保护防止筛选或适配器异常造成零测试假通过，原始结果只保存在本地。
    & $runner --settings Library.runsettings --report-trx --results-directory TestResults --minimum-expected-tests 211 --no-progress
    if ($LASTEXITCODE -ne 0) { throw 'Test execution failed. See TestResults.' }
} finally { Pop-Location }
