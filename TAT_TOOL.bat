@echo off
taskkill /IM gemini_helper_v2_window_activation.exe /F >nul 2>&1
if errorlevel 1 (
    echo Tool khong chay.
) else (
    echo Da tat tool.
)
ping -n 3 127.0.0.1 >nul
