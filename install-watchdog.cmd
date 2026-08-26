@echo off
REM ---------------------------------------------------------------------------
REM Run this ONCE. Double-click is enough - it does not need administrator.
REM
REM It asks Windows to check every 5 minutes whether the bot is running, and to
REM start it if it is not. That covers the cases run-bot.cmd cannot: the loop
REM being killed, Task Manager, a stray Ctrl+C, anything at all.
REM
REM If the bot is already running the check does nothing, so it is harmless.
REM
REM To remove it later:
REM   schtasks /Delete /TN "EnglishBot watchdog" /F
REM ---------------------------------------------------------------------------

echo Watchdog o'rnatilmoqda...
echo.

schtasks /Create /F /TN "EnglishBot watchdog" /SC MINUTE /MO 5 /TR "\"%SystemRoot%\System32\wscript.exe\" \"%~dp0watchdog.vbs\""

echo.
echo ---------------------------------------------------------------
schtasks /Query /TN "EnglishBot watchdog" >nul 2>&1
if errorlevel 1 (
    echo   O'RNATILMADI. Xatolik yuqorida ko'rsatilgan.
) else (
    echo   OK - watchdog o'rnatildi.
    echo   Bot endi har 5 daqiqada tekshiriladi va kerak bo'lsa qayta ishga tushadi.
)
echo ---------------------------------------------------------------
echo.
pause
