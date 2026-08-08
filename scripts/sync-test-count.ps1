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
    [int]$Skipped = 0,
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
)

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$SkippedCount = $Skipped

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

    if ($output -match '已跳过:\s*(\d+)') {
        $SkippedCount = [int]$Matches[1]
    }
    elseif ($output -match 'Skipped:\s*(\d+)') {
        $SkippedCount = [int]$Matches[1]
    }
}
# When caller provides -Count explicitly, derive skipped from 0 (caller intent) but allow -Skipped override
$TotalCount = $Count + $SkippedCount

Write-Host "Test count: $Count (total $TotalCount, skipped $SkippedCount)" -ForegroundColor Green

# V6 (#28): read .NET version info from global.json + Directory.Build.props so docs never drift
$globalJsonPath = Join-Path $RepoRoot "global.json"
$buildPropsPath = Join-Path $RepoRoot "Directory.Build.props"
$SdkVersion = ""
$DotNetMajor = ""
if (Test-Path $globalJsonPath) {
    $gj = [System.IO.File]::ReadAllText($globalJsonPath, [System.Text.Encoding]::UTF8)
    if ($gj -match '"version":\s*"(\d+\.\d+\.\d+)"') { $SdkVersion = $Matches[1] }
    if ($gj -match '"version":\s*"(\d+)\.') { $DotNetMajor = $Matches[1] }
}

# V6 (#28): count DB migration files so README never drifts on migration count
$migrationsDir = Join-Path $RepoRoot "src\CacheHub.Storage\Database\Migrations"
$MigrationCount = 0
if (Test-Path $migrationsDir) {
    $MigrationCount = (Get-ChildItem $migrationsDir -Filter "Migration*.cs" -File).Count
}

# Helper: read/write with UTF-8 (no BOM)
function Update-FileUtf8 {
    param([string]$Path, [scriptblock]$Updater)
    $content = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    $content = & $Updater $content
    [System.IO.File]::WriteAllText($Path, $content, $utf8NoBom)
    Write-Host "  Updated: $Path" -ForegroundColor Yellow
}

# Update AI_DEV_STATE.json
# V7-W08: Use ConvertFrom-Json/ConvertTo-Json instead of regex to prevent corruption
$statePath = Join-Path $RepoRoot "Docs\ai\AI_DEV_STATE.json"
if (Test-Path $statePath) {
    $jsonText = [System.IO.File]::ReadAllText($statePath, [System.Text.Encoding]::UTF8)
    try {
        $state = $jsonText | ConvertFrom-Json
        $state.testCount = $Count
        $state.qualityGates.unitTests = "pass ($Count/$TotalCount, $SkippedCount skipped)"
        # V8-FIX-01: Do NOT regex-replace all digits in currentTask — it corrupts version numbers, task counts, etc.
        # Only update testCount and qualityGates.unitTests (done above).
        # Update architecture test counts — only replace the specific pattern "N tests" or "N passed"
        if ($state.architecture) {
            foreach ($proj in $state.architecture.PSObject.Properties) {
                if ($proj.Value.tests -is [string]) {
                    # Only replace patterns like "965 tests" or "965 passed" — not arbitrary numbers
                    $proj.Value.tests = $proj.Value.tests -replace '^\d+\s+(tests|passed)', "$Count `$1"
                }
            }
        }
        $updatedJson = $state | ConvertTo-Json -Depth 10
        [System.IO.File]::WriteAllText($statePath, $updatedJson, $utf8NoBom)
        Write-Host "  Updated: $statePath" -ForegroundColor Yellow
    }
    catch {
        Write-Warning "Failed to parse AI_DEV_STATE.json as JSON, falling back to regex: $_"
        Update-FileUtf8 $statePath {
            param($json)
            $json = $json -replace '"testCount":\s*\d+', "`"testCount`": $Count"
            # V7-W08: Fixed regex to match full "pass (N/M, X skipped)" pattern including trailing content
            $json = $json -replace 'pass \(\d+/\d+(?:[),]\s*[^"]*)?\)', "pass ($Count/$TotalCount, $SkippedCount skipped)"
            return $json
        }
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
        # V6 (#28): sync .NET version from global.json so docs never drift
        if ($DotNetMajor) {
            $content = $content -replace 'badge/\.NET-\d+\.\d+', "badge/.NET-$DotNetMajor.0"
            $content = $content -replace 'dotnet/\d+\.\d+', "dotnet/$DotNetMajor.0"
            $content = $content -replace '\.NET \d+ \(LTS\)', ".NET $DotNetMajor (LTS)"
        }
        if ($SdkVersion) {
            $content = $content -replace 'SDK \| \d+\.\d+\.\d+', "SDK | $SdkVersion"
        }
        # V6 (#28): sync migration count in project-structure line and ARCHITECTURE-style mentions
        if ($MigrationCount -gt 0) {
            $content = $content -replace '(\d+) 个迁移', "$MigrationCount 个迁移"
        }
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
