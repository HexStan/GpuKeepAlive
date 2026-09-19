$ErrorActionPreference = "Stop"

Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "  GpuKeepAlive - Build & Publish Script" -ForegroundColor Cyan
Write-Host "  (GUI: GpuKeepAlive.exe + CLI: GpuKeepAliveCli.exe)" -ForegroundColor Cyan
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "[ERROR] .NET SDK not found. Please install .NET 10.0 SDK:" -ForegroundColor Red
    Write-Host "https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor Red
    Write-Host ""
    Pause
    exit 1
}

$distDir = Join-Path $PSScriptRoot "dist"

# GUI (WPF) 在 .NET 10 的压缩单文件发布下存在 WPF 内部 P/Invoke 解析缺陷
# (SetWindowLongPtr 抛 DllNotFoundException)，须配合 IncludeNativeLibrariesForSelfExtract 使用。
$targets = @(
    @{ Project = "src\GpuKeepAlive.Gui\GpuKeepAlive.Gui.csproj"; Exe = "GpuKeepAlive.exe"; ExtraArgs = @("-p:IncludeNativeLibrariesForSelfExtract=true") },
    @{ Project = "src\GpuKeepAlive.Cli\GpuKeepAlive.Cli.csproj"; Exe = "GpuKeepAliveCli.exe"; ExtraArgs = @() }
)

foreach ($t in $targets) {
    $projectPath = Join-Path $PSScriptRoot $t.Project
    Write-Host "Publishing $($t.Project) (Release win-x64, single-file)..." -ForegroundColor Green

    & dotnet publish $projectPath -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true @($t.ExtraArgs) -o $distDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "[ERROR] Build failed for $($t.Project). Please check the error messages above." -ForegroundColor Red
        Pause
        exit $LASTEXITCODE
    }

    Copy-Item -Path (Join-Path $distDir $t.Exe) -Destination (Join-Path $PSScriptRoot $t.Exe) -Force
}

Write-Host ""
Write-Host "===================================================" -ForegroundColor Cyan
Write-Host "[SUCCESS] Build completed!" -ForegroundColor Green
Write-Host "Output:"
Write-Host "  $(Join-Path $PSScriptRoot 'GpuKeepAlive.exe')     (GUI - 托盘版)"
Write-Host "  $(Join-Path $PSScriptRoot 'GpuKeepAliveCli.exe')  (CLI - 命令行版)"
Write-Host "===================================================" -ForegroundColor Cyan
Pause
