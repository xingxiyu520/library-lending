param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    dotnet build LibraryLending.sln --configuration $Configuration --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $runner = Join-Path $taskRoot "tests/Library.Tests/bin/$Configuration/net8.0/Library.Tests.exe"
    & $runner --settings Library.runsettings --report-trx --results-directory TestResults --minimum-expected-tests 69 --no-progress
    if ($LASTEXITCODE -ne 0) { throw 'Test execution failed. See TestResults.' }
} finally { Pop-Location }
