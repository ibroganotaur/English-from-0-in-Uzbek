@echo off
REM Stops the bot on purpose. The bot runs hidden, so there is no window to close -
REM this is the way to shut it down. Kill the restart loop first, or it would just
REM start the bot again ten seconds later.

echo Toxtatilmoqda...

for /f "tokens=2 delims=," %%p in ('tasklist /fi "imagename eq cmd.exe" /fo csv /nh 2^>nul') do (
    for /f "tokens=*" %%q in ('wmic process where "ProcessId=%%~p" get CommandLine /value 2^>nul ^| find "run-bot"') do (
        taskkill /pid %%~p /f >nul 2>&1
    )
)

taskkill /im EnglishBot.exe /f >nul 2>&1

echo.
echo Bot toxtatildi.
echo Qayta ishga tushirish uchun: run-bot.cmd (yoki kompyuterni qayta yoqing)
echo.
pause
