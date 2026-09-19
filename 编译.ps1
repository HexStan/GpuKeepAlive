$ErrorActionPreference = "Stop"

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "  GpuKeepAlive - Build & Publish Script" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[ERROR] .NET SDK not found. Please install .NET 10.0 SDK:" -ForegroundColor Red
    Write-Host "https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Red
    Write-Host ""
    Pause
    exit 1
}

$projectPath = Join-Path $PSScriptRoot "GpuKeepAlive\GpuKeepAlive.csproj"
$distDir = Join-Path $PSScriptRoot "dist"
$targetExe = Join-Path $PSScriptRoot "GpuKeepAlive.exe"

Write-Host "Building single-file executable (Release win-x64)..." -ForegroundColor Green
& dotnet publish $projectPath -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o $distDir

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "[ERROR] Build failed. Please check the error messages above." -ForegroundColor Red
    Pause
    exit $LASTEXITCODE
}

$sourceExe = Join-Path $distDir "GpuKeepAlive.exe"
Copy-Item -Path $sourceExe -Destination $targetExe -Force

Write-Host ""
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[SUCCESS] Build completed!" -ForegroundColor Green
Write-Host "Output: $targetExe"
Write-Host "You can now run GpuKeepAlive.exe."
Write-Host "===================================================" -ForegroundColor Cyan
Pause
