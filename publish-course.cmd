@echo off
REM ---------------------------------------------------------------------------
REM Copies the web course into docs\, which is what GitHub Pages serves.
REM
REM The course you edit lives at C:\EnglishCourse\ingliz-tili.html.
REM docs\index.html is only the published copy - never edit that one directly,
REM it gets overwritten every time you run this.
REM
REM After running this:
REM   git add -A && git commit -m "update course" && git push
REM ---------------------------------------------------------------------------

set "SRC=C:\EnglishCourse\ingliz-tili.html"

if not exist "%SRC%" (
    echo.
    echo   Topilmadi: %SRC%
    echo.
    pause
    exit /b 1
)

if not exist "%~dp0docs" mkdir "%~dp0docs"
copy /y "%SRC%" "%~dp0docs\index.html" >nul

echo.
echo   Nusxa olindi -^> docs\index.html
echo   Endi: git add -A ^&^& git commit -m "update course" ^&^& git push
echo.
pause
