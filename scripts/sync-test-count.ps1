<#
.SYNOPSIS
    Syncs test count in documentation files with actual test results.
    V5-W12: Eliminates manual test count updates that cause doc drift.
    V6-FIX: All file I/O now uses -Encoding UTF8 to prevent encoding corruption.

.USAGE
    .\scripts\sync-test-count.ps1
    .\scripts\sync-test-count.ps1 -Count 865
#>

param(
    [int]$Count = 0,
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
)

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

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

# Helper: read/write with UTF-8 (no BOM)
function Update-FileUtf8 {
    param([string]$Path, [scriptblock]$Updater)
    $content = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    $content = & $Updater $content
    [System.IO.File]::WriteAllText($Path, $content, $utf8NoBom)
    Write-Host "  Updated: $Path" -ForegroundColor Yellow
}

# Update AI_DEV_STATE.json
$statePath = Join-Path $RepoRoot "Docs\ai\AI_DEV_STATE.json"
if (Test-Path $statePath) {
    Update-FileUtf8 $statePath {
        param($json)
        $json = $json -replace '"testCount":\s*\d+', "`"testCount`": $Count"
        $json = $json -replace 'pass \(\d+/', "pass ($Count/"
        return $json
    }
}

# Update README.md
$readmePath = Join-Path $RepoRoot "README.md"
if (Test-Path $readmePath) {
    Update-FileUtf8 $readmePath {
        param($content)
        $content = $content -replace 'Tests-\d+%20passed', "Tests-$Count%20passed"
        $content = $content -replace '\d+ 测试通过', "$Count 测试通过"
        $content = $content -replace '# \d+ 测试', "# $Count 测试"
        $content = $content -replace '\d+ 通过 \| 覆盖全部模块', "$Count 通过 | 覆盖全部模块"
        $content = $content -replace '通过 \d+ 个测试验证', "通过 $Count 个测试验证"
        return $content
    }
}

# Update AGENTS.md
$agentsPath = Join-Path $RepoRoot "AGENTS.md"
if (Test-Path $agentsPath) {
    Update-FileUtf8 $agentsPath {
        param($content)
        $content = $content -replace '\d+ tests', "$Count tests"
        return $content
    }
}

# Update CONTRIBUTING.md
$contribPath = Join-Path $RepoRoot "CONTRIBUTING.md"
if (Test-Path $contribPath) {
    Update-FileUtf8 $contribPath {
        param($content)
        $content = $content -replace '\d+ 测试', "$Count 测试"
        return $content
    }
}

# Update ARCHITECTURE.md
$archPath = Join-Path $RepoRoot "Docs\ARCHITECTURE.md"
if (Test-Path $archPath) {
    Update-FileUtf8 $archPath {
        param($content)
        $content = $content -replace '\| 单元测试 \| \d+', "| 单元测试 | $Count"
        return $content
    }
}

Write-Host "`nDone. All docs synced to $Count tests." -ForegroundColor Green
