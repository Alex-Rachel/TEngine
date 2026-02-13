@echo off
chcp 65001 >nul
setlocal

REM 设置错误处理 - 确保脚本出错时不会一闪而过
if not defined IN_SUBPROCESS (
    set IN_SUBPROCESS=1
    cmd /k "%~f0 %*"
    exit /b
)

echo ==========================================
echo Fantasy Protocol Export Tool 2025.2.1419
echo ==========================================
echo.

REM 获取脚本所在目录
set "SCRIPT_DIR=%~dp0"
set "APP_DLL=%SCRIPT_DIR%Fantasy.ProtocolExportTool.dll"

REM 检查 dotnet 是否安装
where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo ==========================================
    echo 错误：未检测到 .NET 运行时
    echo ==========================================
    echo.
    echo 请先安装 .NET 8.0 SDK 或 Runtime
    echo.
    echo 下载地址：
    echo   https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    goto :error
)

REM 检查 .NET 版本
for /f "tokens=1 delims=." %%v in ('dotnet --version 2^>nul') do set MAJOR_VERSION=%%v

if not defined MAJOR_VERSION (
    echo.
    echo ==========================================
    echo 错误：无法获取 .NET 版本
    echo ==========================================
    echo.
    goto :error
)

if %MAJOR_VERSION% LSS 8 (
    echo.
    echo ==========================================
    echo 错误：.NET 版本过低
    echo ==========================================
    echo.
    dotnet --version
    echo 需要版本: 8.0 或更高
    echo.
    echo 请升级到 .NET 8.0 或更高版本
    echo.
    echo 下载地址：
    echo   https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    goto :error
)

dotnet --version
echo.
echo 正在启动导出工具...
echo.

REM 运行导出工具
dotnet "%APP_DLL%" export --silent

if errorlevel 1 (
    echo.
    echo ==========================================
    echo × 导出失败
    echo ==========================================
    echo.
    echo 提示：请检查 ExporterSettings.json 配置文件是否正确
    echo.
    goto :error
)

echo.
echo ==========================================
echo √ 导出完成！
echo ==========================================
echo.
echo 按任意键退出...
pause >nul
exit /b 0

:error
echo.
echo 按任意键退出...
pause >nul
exit /b 1
