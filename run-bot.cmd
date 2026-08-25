@echo off
REM ---------------------------------------------------------------------------
REM Starts the English bot from the published Release build.
REM
REM Her progress does NOT live here - it is in %LOCALAPPDATA%\EnglishBot, so
REM rebuilding or republishing this folder can never erase her streak.
REM
REM Rebuild after changing code or lessons:
REM   "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" publish -c Release -o publish
REM ---------------------------------------------------------------------------

setlocal
set "EXE=%~dp0publish\EnglishBot.exe"

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

title Noldan Ingliz Tili - bot
cd /d "%~dp0publish"

:run
REM Full path, not a bare name: cmd does not always search the current folder.
"%EXE%"

REM A crash or a dropped network should not end her evening reminder.
REM ping, not timeout: timeout aborts when stdin is redirected.
echo.
echo   Bot toxtadi. 10 soniyadan keyin qayta ishga tushadi... (Ctrl+C = chiqish)
ping -n 11 127.0.0.1 >nul 2>&1
goto run
