@echo off
setlocal
set "SPTPATH=%~1"
if "%SPTPATH%"=="" set "SPTPATH=C:\spt 4.1"
echo Building SPT Korean Addon for: %SPTPATH%
if "%~2"=="" (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD_DISTRIBUTION.ps1" -SPTPath "%SPTPATH%"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD_DISTRIBUTION.ps1" -SPTPath "%SPTPATH%" -PythonExe "%~2"
)
if errorlevel 1 (
  echo.
  echo BUILD FAILED. Leave this window open and send the error output.
  pause
  exit /b %errorlevel%
)
echo.
echo DONE. Open the _dist folder and install the generated ZIP into the SPT root.
pause
exit /b 0
