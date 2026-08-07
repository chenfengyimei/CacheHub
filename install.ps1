# CacheHub 安装脚本 (PowerShell)

param(
    [switch]$SkipTests,
    [switch]$Help
)

if ($Help) {
    Write-Host "Usage: install.ps1 [-SkipTests] [-Help]"
    Write-Host "  -SkipTests  Skip test suite (not recommended for production)"
    Write-Host "  -Help       Show this help message"
    exit 0
}

Write-Host "CacheHub Installation" -ForegroundColor Cyan
Write-Host "===================" -ForegroundColor Cyan
Write-Host ""

# Check .NET SDK
$dotnetVersion = dotnet --version 2>$null
if (!$dotnetVersion) {
    Write-Host "Error: .NET SDK not found. Please install .NET 9 SDK." -ForegroundColor Red
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/10.0"
    exit 1
}
Write-Host "[1/4] .NET SDK: $dotnetVersion" -ForegroundColor Green

# Build
Write-Host "[2/4] Building CacheHub..." -ForegroundColor Yellow
dotnet build CacheHub.sln -c Release --nologo 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Build failed." -ForegroundColor Red
    exit 1
}
Write-Host "  Build successful." -ForegroundColor Green

# Test
if (!$SkipTests) {
    Write-Host "[3/4] Running tests..." -ForegroundColor Yellow
    dotnet test CacheHub.sln -c Release --no-build --nologo --verbosity quiet 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Error: Tests failed. Aborting installation." -ForegroundColor Red
        Write-Host "  Use -SkipTests flag to bypass (not recommended for production)." -ForegroundColor Yellow
        exit 1
    } else {
        Write-Host "  All tests passed." -ForegroundColor Green
    }
} else {
    Write-Host "[3/4] Skipping tests (-SkipTests flag set)." -ForegroundColor Yellow
}

# Publish single-file
Write-Host "[4/4] Publishing single-file executable..." -ForegroundColor Yellow
$publishDir = "$PSScriptRoot\publish"
dotnet publish src/CacheHub.Cli/CacheHub.Cli.csproj -c Release -o $publishDir --nologo 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Publish failed." -ForegroundColor Red
    exit 1
}

$exePath = Join-Path $publishDir "cachehub.exe"
if (Test-Path $exePath) {
    Write-Host "  Published: $exePath" -ForegroundColor Green
    Write-Host ""
    Write-Host "To use CacheHub, add the publish directory to your PATH:" -ForegroundColor Cyan
    Write-Host "  `$env:PATH += ';$publishDir'" -ForegroundColor White
    Write-Host ""
    Write-Host "Or copy cachehub.exe to a directory already in your PATH." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Verify installation:" -ForegroundColor Cyan
    Write-Host "  cachehub version" -ForegroundColor White
    Write-Host "  cachehub capabilities" -ForegroundColor White
    Write-Host "  cachehub integration verify" -ForegroundColor White
} else {
    Write-Host "Error: cachehub.exe not found in publish directory." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
