@echo off
REM 编译 JXR2PNG.exe
REM 需安装 .NET SDK: https://dotnet.microsoft.com/download

set DOTNET=dotnet
where dotnet >nul 2>&1 || set "DOTNET=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET%" (
    echo 未找到 dotnet，请将 .NET SDK 安装目录加入 PATH
    exit /b 1
)

"%DOTNET%" build -c Release
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%

REM 发布为单 exe（可选）
"%DOTNET%" publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
echo.
echo 输出: bin\Release\net10.0\JXR2PNG.exe
echo 单exe: bin\Release\net10.0\win-x64\publish\JXR2PNG.exe
