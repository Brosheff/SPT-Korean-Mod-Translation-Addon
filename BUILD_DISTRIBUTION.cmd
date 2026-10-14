@echo off
setlocal EnableExtensions DisableDelayedExpansion
rem Build from this project's own folder. Do not locate, ask for, or modify an installed game.
rem Optional: BUILD_DISTRIBUTION.cmd ["local references folder"] ["python executable"]
set "SCRIPT=%~dp0BUILD_DISTRIBUTION.ps1"
if not exist "%SCRIPT%" (
    echo ERROR: BUILD_DISTRIBUTION.ps1 was not found beside this CMD.
    exit /b 1
)
where powershell.exe >nul 2>&1
if errorlevel 1 (
    echo ERROR: Windows PowerShell is unavailable.
    exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
set "STATUS=%ERRORLEVEL%"
if not "%STATUS%"=="0" (
    echo.
    echo BUILD FAILED. See the error above.
    if not defined CI pause
    exit /b %STATUS%
)
echo.
echo BUILD FINISHED. Release ZIP is in _dist beside this CMD.
if not defined CI pause
exit /b 0
