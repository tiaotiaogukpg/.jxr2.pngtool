@echo off
chcp 936 >nul
setlocal enabledelayedexpansion

REM HDR to SDR: ffmpeg npl=200 tonemap=hable bt709
REM No args: batch JXR to PNG, overwrite, delete jxr
REM Arg1=folder: scan that folder
REM Arg1=file: single file convert

where ffmpeg >nul 2>&1
if errorlevel 1 (
    echo 错误: 未找到 ffmpeg，请确保已安装并加入 PATH
    pause
    exit /b 1
)

set "ARG1=%~1"
set "ARG2=%~2"

REM No arg or arg1=existing folder: batch JXR mode
if "%ARG1%"=="" (
    set "TARGET=%~dp0"
    goto :batch_jxr
)
if exist "%ARG1%\." (
    set "TARGET=%~1"
    if not "!TARGET:~-1!"=="\" set "TARGET=!TARGET!\"
    goto :batch_jxr
)

REM Single file mode
set "INPUT=%ARG1%"
set "OUTPUT=%ARG2%"
if not exist "%INPUT%" (
    echo 错误: 找不到文件 "%INPUT%"
    pause
    exit /b 1
)
if "%OUTPUT%"=="" (
    set "BASE=%~dpn1"
    set "EXT=%~x1"
    if /i "!EXT!"==".jxr" (set "OUTPUT=!BASE!.png") else if /i "!EXT!"==".wdp" (set "OUTPUT=!BASE!.png") else if /i "!EXT!"==".hdp" (set "OUTPUT=!BASE!.png") else (set "OUTPUT=!BASE!_sdr!EXT!")
)
echo 输入: %INPUT%
echo 输出: %OUTPUT%
echo.
goto :convert_single

:batch_jxr
cd /d "%TARGET%"
if errorlevel 1 (
    echo 错误: 无法进入目录 "%TARGET%"
    pause
    exit /b 1
)
echo Target: %TARGET%
echo.
set "n=0"
for %%F in (*.jxr) do set /a n+=1
if %n%==0 (
    echo No .jxr files found
    pause
    exit /b 0
)
echo Found %n% .jxr file(s)
echo.
set "i=0"
set "VF=zscale=t=linear:npl=200,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=pc,format=rgba"
set "PS1=%~dp0JXR2PNG.ps1"
for %%F in (*.jxr) do (
    set /a i+=1
    set "jxr=%%~fF"
    set "png=%%~dpnF.png"
    echo [!i!/%n%] %%~nxF
    ffmpeg -y -loglevel error -i "!jxr!" -vf "!VF!" -frames:v 1 "!png!" 2>nul
    if not exist "!png!" (
        if exist "!PS1!" (
            powershell -NoProfile -ExecutionPolicy Bypass -File "!PS1!" "!jxr!" 1>nul 2>&1
        )
    )
    if exist "!png!" (
        del "%%F" 2>nul
        echo       OK, PNG saved, jxr deleted
    ) else (
        echo       FAILED
    )
    echo.
)
echo ========================================
echo Done
pause
exit /b 0

:convert_single
set "VF_IMG=zscale=t=linear:npl=200,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=pc,format=rgba"
set "VF_VID=zscale=t=linear:npl=200,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p"
set "EXT=%~x1"
set "IS_IMG=0"
for %%x in (png jpg jpeg bmp tiff tif jxr wdp hdp) do if /i "!EXT!"==".%%x" set "IS_IMG=1"
if "!IS_IMG!"=="1" (
    ffmpeg -y -i "%INPUT%" -vf "!VF_IMG!" -frames:v 1 "%OUTPUT%"
) else (
    ffmpeg -y -i "%INPUT%" -vf "!VF_VID!" -c:v libx264 -crf 18 -preset medium -c:a copy "%OUTPUT%"
)

:single_done
if errorlevel 1 (
    echo 转换失败
    pause
    exit /b 1
)
echo 完成: %OUTPUT%
pause
exit /b 0
