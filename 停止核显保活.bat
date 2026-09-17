@echo off
taskkill /f /im GpuKeepAlive.exe >nul 2>nul
if errorlevel 1 (
    echo [Info] GpuKeepAlive is not running.
) else (
    echo [OK] GpuKeepAlive stopped successfully.
)
ping 127.0.0.1 -n 2 >nul
