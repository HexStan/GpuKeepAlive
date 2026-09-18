[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$exePath = Join-Path $PSScriptRoot "GpuKeepAlive.exe"

if (-not (Test-Path -Path $exePath -PathType Leaf)) {
    Write-Host "[ERROR] 未找到 $exePath" -ForegroundColor Red
    Write-Host "请先运行 编译.ps1 编译生成可执行文件。" -ForegroundColor Yellow
    Pause
    exit 1
}

if ($RemainingArgs -and $RemainingArgs.Count -gt 0) {
    & $exePath @RemainingArgs
} else {
    & $exePath -a intel -f 15 -i 1
}

Pause
