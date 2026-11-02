# 执行真实Roslyn安全分析和依赖漏洞检查；原始结果仅保存在本地忽略目录。
param([string]$OutputDirectory = 'evidence/sprint-3/security')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
    # 固定输出到当前工作区，保留编译日志及SARIF供报告追溯。
    $output = [IO.Path]::GetFullPath((Join-Path $taskRoot $OutputDirectory))
    if (!$output.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Security output must stay inside this workspace.'
    }
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $sarif = Join-Path $output 'final-security.sarif'
    # Rebuild确保分析器重新运行，警告视为失败，禁止跳过真实扫描。
    dotnet build src/Library.Web/Library.Web.csproj --configuration Release --no-incremental --warnaserror "-p:ErrorLog=$sarif,version=2.1" *> (Join-Path $output 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Security build failed. Inspect the local SARIF and build log.' }
    # 包括传递依赖；结果报告范围是NuGet已公布的漏洞数据。
    dotnet list LibraryLending.sln package --vulnerable --include-transitive --format json *> (Join-Path $output 'dependencies.json')
    if ($LASTEXITCODE -ne 0) { throw 'Dependency audit failed.' }
    $audit = Get-Content -LiteralPath (Join-Path $output 'dependencies.json') -Raw | ConvertFrom-Json
    $affected = @($audit.projects | ForEach-Object { $_.frameworks } | ForEach-Object { $_.topLevelPackages; $_.transitivePackages } | Where-Object { $_.vulnerabilities })
    if ($affected.Count -gt 0) { throw 'Known vulnerable packages were reported. Inspect dependencies.json.' }
    Write-Output "Security analysis completed: $output"
} finally { Pop-Location }
