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

cd /d "%~dp0publish"

if not exist "EnglishBot.exe" (
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

:run
EnglishBot.exe

REM A crash or a dropped network should not end her evening reminder.
echo.
echo   Bot toxtadi. 10 soniyadan keyin qayta ishga tushadi... (Ctrl+C = chiqish)
timeout /t 10 /nobreak >nul
goto run
