@echo off
set found=0
taskkill /IM GeminiQuickOverlay.exe /F >nul 2>&1
if not errorlevel 1 set found=1
taskkill /IM gemini_helper_v2_window_activation.exe /F >nul 2>&1
if not errorlevel 1 set found=1
if "%found%"=="1" (
    echo Da tat tool.
) else (
    echo Tool khong chay.
)
ping -n 3 127.0.0.1 >nul
