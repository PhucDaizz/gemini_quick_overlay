@echo off
if "%1"=="h" goto begin
start mshta vbscript:createobject("wscript.shell").run("""%~nx0"" h",0)(window.close)&&exit
:begin

:: --- PHAN 1: TIM DUONG DAN CHROME ---
set "CHROME_EXE="

:: 1. Uu tien doc tu file cau hinh (neu nguoi dung tu tao)
if exist "%~dp0chrome_path.txt" (
    set /p CHROME_EXE=<"%~dp0chrome_path.txt"
)

:: 2. Neu khong co file cau hinh, tu dong do tim 3 vi tri mac dinh
if "%CHROME_EXE%"=="" (
    if exist "C:\Program Files\Google\Chrome\Application\chrome.exe" (
        set "CHROME_EXE=C:\Program Files\Google\Chrome\Application\chrome.exe"
    ) else if exist "C:\Program Files (x86)\Google\Chrome\Application\chrome.exe" (
        set "CHROME_EXE=C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
    ) else if exist "%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe" (
        set "CHROME_EXE=%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"
    )
)

:: 3. Neu van khong tim thay, bao loi
if "%CHROME_EXE%"=="" (
    mshta vbscript:Execute("msgbox ""Khong tim thay Google Chrome! Vui long tao file chrome_path.txt va dan duong dan Chrome vao do."", 16, ""Loi Khoi Dong"":close")
    exit
)

:: --- PHAN 2: KHOI DONG CHROME ---
:: Check xem port 9222 da mo chua
netstat -an | find "9222" >nul
if errorlevel 1 (
    start "" "%CHROME_EXE%" --remote-debugging-port=9222 --user-data-dir="C:\selenium\ChromeProfile" --disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding
    timeout /t 2 >nul
)

:: --- PHAN 3: CHAY TOOL PYTHON ---
cd /d "%~dp0"
:: Neu da dong goi exe thi doi thanh ten file exe
if exist gemini_helper_v2_window_activation.exe (
    start "" gemini_helper_v2_window_activation.exe
) else (
    start "" pythonw gemini_helper_v2_window_activation.py
)
