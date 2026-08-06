# AI_KV 安装脚本 (PowerShell)

Write-Host "AI_KV Installation" -ForegroundColor Cyan
Write-Host "===================" -ForegroundColor Cyan
Write-Host ""

# Check .NET SDK
$dotnetVersion = dotnet --version 2>$null
if (!$dotnetVersion) {
    Write-Host "Error: .NET SDK not found. Please install .NET 9 SDK." -ForegroundColor Red
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/9.0"
    exit 1
}
Write-Host "[1/4] .NET SDK: $dotnetVersion" -ForegroundColor Green

# Build
Write-Host "[2/4] Building AI_KV..." -ForegroundColor Yellow
dotnet build AI_KV.sln -c Release --nologo 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Build failed." -ForegroundColor Red
    exit 1
}
Write-Host "  Build successful." -ForegroundColor Green

# Test
Write-Host "[3/4] Running tests..." -ForegroundColor Yellow
dotnet test AI_KV.sln -c Release --no-build --nologo --verbosity quiet 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Warning: Some tests failed. Continuing anyway." -ForegroundColor Yellow
} else {
    Write-Host "  All tests passed." -ForegroundColor Green
}

# Publish single-file
Write-Host "[4/4] Publishing single-file executable..." -ForegroundColor Yellow
$publishDir = "$PSScriptRoot\publish"
dotnet publish src/AiKv.Cli/AiKv.Cli.csproj -c Release -o $publishDir --nologo 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Publish failed." -ForegroundColor Red
    exit 1
}

$exePath = Join-Path $publishDir "aikv.exe"
if (Test-Path $exePath) {
    Write-Host "  Published: $exePath" -ForegroundColor Green
    Write-Host ""
    Write-Host "To use AI_KV, add the publish directory to your PATH:" -ForegroundColor Cyan
    Write-Host "  `$env:PATH += ';$publishDir'" -ForegroundColor White
    Write-Host ""
    Write-Host "Or copy aikv.exe to a directory already in your PATH." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Verify installation:" -ForegroundColor Cyan
    Write-Host "  aikv version" -ForegroundColor White
    Write-Host "  aikv capabilities" -ForegroundColor White
    Write-Host "  aikv integration verify" -ForegroundColor White
} else {
    Write-Host "Error: aikv.exe not found in publish directory." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
