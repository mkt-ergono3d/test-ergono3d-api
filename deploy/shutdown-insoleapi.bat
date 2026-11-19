@echo off
setlocal EnableExtensions
cd /d "%~dp0"

REM Relaunch elevated when started by double-click.
fltmc >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath $env:ComSpec -ArgumentList '/d','/c','""%~f0""' -Verb RunAs"
    exit /b
)

call "%~dp0watch-health-and-restart.bat" --shutdown
set "EXIT_CODE=%ERRORLEVEL%"
echo.
if "%EXIT_CODE%"=="0" (
    echo [SUCCESS] The API monitor and InsoleApi are stopped. Rhino was not changed.
) else (
    echo [FAILED] Shutdown did not complete. See watch-health-and-restart.log.
)
pause
exit /b %EXIT_CODE%
