@echo off
echo Dang khoi dong Chrome o che do Debugging + BACKGROUND MODE...
echo Vui long tat het cac cua so Chrome khac truoc khi chay file nay lan dau.
"C:\Program Files\Google\Chrome\Application\chrome.exe" --remote-debugging-port=9222 --user-data-dir="C:\selenium\ChromeProfile" --disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding
pause
