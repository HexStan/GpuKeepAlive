$processes = Get-Process -Name "GpuKeepAlive" -ErrorAction SilentlyContinue

if ($processes) {
    $processes | Stop-Process -Force
    Write-Host "[OK] GpuKeepAlive stopped successfully." -ForegroundColor Green
} else {
    Write-Host "[Info] GpuKeepAlive is not running." -ForegroundColor Yellow
}

Start-Sleep -Seconds 1
