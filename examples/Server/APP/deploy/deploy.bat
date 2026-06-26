@echo off
chcp 65001 >nul
setlocal

rem ==== Locate Git Bash (works regardless of install drive) ====
set "BASH="
for /f "delims=" %%i in ('where git 2^>nul') do if not defined GITEXE set "GITEXE=%%i"
if defined GITEXE for %%a in ("%GITEXE%") do set "BASH=%%~dpa..\bin\bash.exe"
if not exist "%BASH%" set "BASH=C:\Program Files\Git\bin\bash.exe"
if not exist "%BASH%" set "BASH=C:\Program Files (x86)\Git\bin\bash.exe"
if not exist "%BASH%" set "BASH=%LOCALAPPDATA%\Programs\Git\bin\bash.exe"
if not exist "%BASH%" (
  echo [ERROR] Git Bash not found. Install Git for Windows:
  echo         https://git-scm.com/download/win
  pause
  exit /b 1
)

rem ==== Convert this script's dir to forward slashes for bash ====
set "SELF=%~dp0deploy.local.sh"
set "SELF=%SELF:\=/%"

echo ============================================
echo   Fantasy one-click deploy
echo   publish + upload + restart
echo ============================================
"%BASH%" -l "%SELF%" %*

echo.
echo ===== Done. Press any key to close. =====
pause >nul
