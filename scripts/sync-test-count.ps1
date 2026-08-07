<#
.SYNOPSIS
    Syncs test count in documentation files with actual test results.
    V5-W12: Eliminates manual test count updates that cause doc drift.

.USAGE
    .\scripts\sync-test-count.ps1
    .\scripts\sync-test-count.ps1 -Count 847
#>

param(
    [int]$Count = 0,
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
)

if ($Count -eq 0) {
    Write-Host "Running tests to get current count..." -ForegroundColor Cyan
    $output = dotnet test (Join-Path $RepoRoot "CacheHub.sln") -c Release --nologo -v q 2>&1 | Out-String

    if ($output -match '通过:\s*(\d+)') {
        $Count = [int]$Matches[1]
    } elseif ($output -match 'Passed:\s*(\d+)') {
        $Count = [int]$Matches[1]
    } else {
        Write-Error "Could not parse test count. Run 'dotnet test' manually and pass -Count=<n>"
        exit 1
    }
}

Write-Host "Test count: $Count" -ForegroundColor Green

# Update AI_DEV_STATE.json
$statePath = Join-Path $RepoRoot "Docs\ai\AI_DEV_STATE.json"
if (Test-Path $statePath) {
    $json = Get-Content $statePath -Raw
    $json = $json -replace '"testCount":\s*\d+', "`"testCount`": $Count"
    $json = $json -replace 'pass \(\d+/', "pass ($Count/"
    Set-Content $statePath -Value $json -NoNewline
    Write-Host "  Updated: Docs/ai/AI_DEV_STATE.json" -ForegroundColor Yellow
}

# Update README.md
$readmePath = Join-Path $RepoRoot "README.md"
if (Test-Path $readmePath) {
    $content = Get-Content $readmePath -Raw
    $content = $content -replace 'Tests-\d+%20passed', "Tests-$Count%20passed"
    $content = $content -replace '\d+ 测试通过', "$Count 测试通过"
    $content = $content -replace '# \d+ 测试', "# $Count 测试"
    $content = $content -replace '\d+ 通过 \| 覆盖全部模块', "$Count 通过 | 覆盖全部模块"
    $content = $content -replace '通过 \d+ 个测试验证', "通过 $Count 个测试验证"
    Set-Content $readmePath -Value $content -NoNewline
    Write-Host "  Updated: README.md" -ForegroundColor Yellow
}

# Update AGENTS.md
$agentsPath = Join-Path $RepoRoot "AGENTS.md"
if (Test-Path $agentsPath) {
    $content = Get-Content $agentsPath -Raw
    $content = $content -replace '\d+ tests', "$Count tests"
    Set-Content $agentsPath -Value $content -NoNewline
    Write-Host "  Updated: AGENTS.md" -ForegroundColor Yellow
}

# Update CONTRIBUTING.md
$contribPath = Join-Path $RepoRoot "CONTRIBUTING.md"
if (Test-Path $contribPath) {
    $content = Get-Content $contribPath -Raw
    $content = $content -replace '\d+ 测试', "$Count 测试"
    Set-Content $contribPath -Value $content -NoNewline
    Write-Host "  Updated: CONTRIBUTING.md" -ForegroundColor Yellow
}

# Update ARCHITECTURE.md
$archPath = Join-Path $RepoRoot "Docs\ARCHITECTURE.md"
if (Test-Path $archPath) {
    $content = Get-Content $archPath -Raw
    $content = $content -replace '\| 单元测试 \| \d+', "| 单元测试 | $Count"
    Set-Content $archPath -Value $content -NoNewline
    Write-Host "  Updated: Docs/ARCHITECTURE.md" -ForegroundColor Yellow
}

Write-Host "`nDone. All docs synced to $Count tests." -ForegroundColor Green
