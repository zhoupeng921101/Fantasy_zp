@echo off
chcp 65001 >nul

REM 设置错误处理 - 确保脚本出错时不会一闪而过
if not defined IN_SUBPROCESS (
    set IN_SUBPROCESS=1
    cmd /k "%~f0 %*"
    exit /b
)

echo ==========================================
echo Fantasy Protocol Export Tool 2026.0.1017
echo ==========================================
echo.

set "SCRIPT_DIR=%~dp0"

REM ExporterSettings.json 以相对路径锚定本目录(cwd),必须先切入再执行
cd /d "%SCRIPT_DIR%"

dotnet "%SCRIPT_DIR%Fantasy.ProtocolExportTool.dll" export --silent

echo.
echo 按任意键退出...
pause >nul
exit /b 1