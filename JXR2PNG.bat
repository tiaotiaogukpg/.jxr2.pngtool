@echo off
chcp 936 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"

echo ========================================
echo   JXR 转 PNG 批量转换
echo ========================================
echo.

set "n=0"
for %%F in (*.jxr) do set /a n+=1
if %n%==0 (
    echo 当前目录下未找到 .jxr 文件。
    pause
    exit /b 0
)

echo 找到 %n% 个 .jxr 文件，开始处理...
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
        echo       转换完成，已删除 .jxr
    ) else (
        echo       转换失败
    )
    echo.
)

echo ========================================
echo   全部完成
echo ========================================
pause
