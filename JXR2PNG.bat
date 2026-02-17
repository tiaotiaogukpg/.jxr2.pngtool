@echo off
chcp 936 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"

REM ????????? JXR2PNG.exe????????????? .jxr?
if exist "%~dp0JXR2PNG.exe" (
    "%~dp0JXR2PNG.exe"
    echo.
    pause
    exit /b 0
)

REM ? exe ???? PowerShell ??
echo ========================================
echo   JXR to PNG
echo ========================================
echo.

set "n=0"
for %%F in (*.jxr) do set /a n+=1
if %n%==0 (
    echo ???????? .jxr ??
    pause
    exit /b 0
)

echo ?? %n% ? .jxr ???????...
echo.

set "ps1=%~dp0JXR2PNG.ps1"
set "i=0"
for %%F in (*.jxr) do (
    set /a i+=1
    set "jxrPath=%%~fF"
    set "pngPath=%%~dpnF.png"
    echo [%%i/%n%] %%F
    powershell -NoProfile -NoLogo -ExecutionPolicy Bypass -File "!ps1!" "!jxrPath!" 1>nul
    if exist "!pngPath!" (
        del "%%F"
        echo       ???????? .jxr
    ) else (
        echo       ????
    )
    echo.
)

echo ========================================
echo   ????
echo ========================================
pause
