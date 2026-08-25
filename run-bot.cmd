@echo off
REM ---------------------------------------------------------------------------
REM Starts the English bot from the published Release build.
REM
REM Output goes to bot-log.txt, NOT to this window. That is deliberate: a console
REM window in selection mode (one stray click inside it) blocks the next write to
REM stdout, which freezes the whole bot mid-Console.WriteLine. A redirected file
REM handle cannot block that way. It also means there is always a log to read.
REM
REM Her progress does NOT live here - it is in %LOCALAPPDATA%\EnglishBot, so
REM rebuilding or republishing this folder can never erase her streak.
REM
REM Rebuild after changing code or lessons:
REM   "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" publish -c Release -o publish
REM ---------------------------------------------------------------------------

setlocal
set "EXE=%~dp0publish\EnglishBot.exe"
set "LOG=%~dp0bot-log.txt"
set "PREV=%~dp0bot-log.prev.txt"

if not exist "%EXE%" (
    echo.
    echo   publish\EnglishBot.exe topilmadi.
    echo   Avval quyidagini ishga tushiring:
    echo.
    echo     "%%LOCALAPPDATA%%\Microsoft\dotnet\dotnet.exe" publish -c Release -o publish
    echo.
    pause
    exit /b 1
)

REM Keep one previous run, so the log never grows without bound.
if exist "%PREV%" del "%PREV%"
if exist "%LOG%" move /y "%LOG%" "%PREV%" >nul

title Noldan Ingliz Tili - bot
cd /d "%~dp0publish"

:run
echo ---------------------------------------------------------------- >> "%LOG%"
echo Ishga tushdi: %DATE% %TIME% >> "%LOG%"
echo ---------------------------------------------------------------- >> "%LOG%"

REM Full path, not a bare name: cmd does not always search the current folder.
"%EXE%" >> "%LOG%" 2>&1

REM A crash or a dropped network should not end her evening reminder.
REM ping, not timeout: timeout aborts when stdin is redirected.
echo. >> "%LOG%"
echo Bot toxtadi (%DATE% %TIME%). 10 soniyadan keyin qayta ishga tushadi. >> "%LOG%"
ping -n 11 127.0.0.1 >nul 2>&1
goto run
