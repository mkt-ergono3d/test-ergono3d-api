@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM InsoleApi-only health monitor. Rhino is never inspected or changed.

set "MONITOR_ID=production"
REM Keep this existing name so re-running the BAT replaces the old task.
set "TASK_NAME=Ergono3D Health Watch - %MONITOR_ID%"
set "MONITOR_SCRIPT=%~f0"

set "API_EXE=C:\Users\Administrator\Desktop\web\insole-api\publish\publish\InsoleApi.exe"
set "API_ARGS="
set "HEALTH_URL=https://api.ergono3d.com/health"
set "CHECK_INTERVAL_SECONDS=300"
set "HTTP_TIMEOUT_SECONDS=20"
set "API_STARTUP_GRACE_SECONDS=30"
set "STOP_TIMEOUT_SECONDS=20"
set "LOG_PATH=%~dp0watch-health-and-restart.log"
set "TASK_OUTPUT=%TEMP%\Ergono3DApiWatch-%RANDOM%.log"

if /i "%~1"=="--run" goto :Run
if /i "%~1"=="--shutdown" goto :Shutdown
if /i "%~1"=="--uninstall" goto :Uninstall
if /i "%~1"=="--help" goto :Help

REM Default operation is idempotent: replace the monitor task and leave a
REM healthy running API untouched. It never starts a second monitor instance.
call :StopMonitor
if errorlevel 1 goto :Failed
call :TestHealth
if errorlevel 1 (
    call :Log "Health check failed during manual startup. Restarting InsoleApi."
    call :RestartApi
    if errorlevel 1 goto :Failed
    call :Log "InsoleApi restart requested; waiting %API_STARTUP_GRACE_SECONDS%s before the monitor begins."
    timeout /t %API_STARTUP_GRACE_SECONDS% /nobreak >nul
)
call :CreateAndStartTask
if errorlevel 1 goto :Failed
echo [SUCCESS] API monitor is registered and running.
echo Task: %TASK_NAME%
echo Log:  %LOG_PATH%
exit /b 0


:Run
if not exist "%API_EXE%" (
    call :Log "Configuration error: API_EXE does not exist: %API_EXE%"
    exit /b 2
)

call :Log "Monitor started. Health=%HEALTH_URL%; interval=%CHECK_INTERVAL_SECONDS%s."

:Loop
call :TestHealth
if errorlevel 1 (
    call :Log "Health check failed. Restarting InsoleApi."
    call :RestartApi
    if errorlevel 1 call :Log "InsoleApi restart failed."
) else (
    call :Log "Health check passed."
)
timeout /t %CHECK_INTERVAL_SECONDS% /nobreak >nul
goto :Loop


:TestHealth
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try{$response=Invoke-WebRequest -Uri $env:HEALTH_URL -TimeoutSec ([int]$env:HTTP_TIMEOUT_SECONDS) -UseBasicParsing -Headers @{'Cache-Control'='no-cache'} -ErrorAction Stop; if($response.StatusCode -ge 200 -and $response.StatusCode -lt 300){exit 0}; exit 1}catch{exit 1}"
exit /b %errorlevel%


:StartApi
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try{$exe=$env:API_EXE; $workingDirectory=Split-Path -Parent $exe; if([string]::IsNullOrWhiteSpace($env:API_ARGS)){Start-Process -FilePath $exe -WorkingDirectory $workingDirectory -WindowStyle Hidden | Out-Null}else{Start-Process -FilePath $exe -ArgumentList $env:API_ARGS -WorkingDirectory $workingDirectory -WindowStyle Hidden | Out-Null}; exit 0}catch{exit 1}"
if errorlevel 1 (
    call :Log "InsoleApi start command failed."
    exit /b 1
)
call :Log "InsoleApi start command issued."
exit /b 0


:StopApi
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try{$target=[IO.Path]::GetFullPath($env:API_EXE); Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {$_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath) -ieq $target} | ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue}; exit 0}catch{exit 1}"
if errorlevel 1 exit /b 1
timeout /t 5 /nobreak >nul
call :Log "InsoleApi stop command issued."
exit /b 0


:RestartApi
call :StopApi
if errorlevel 1 (
    call :Log "InsoleApi did not stop within %STOP_TIMEOUT_SECONDS%s."
    exit /b 1
)
timeout /t 5 /nobreak >nul
call :StartApi
exit /b %errorlevel%


:StopMonitor
REM Disable/end the current scheduled task first, then remove a same-script
REM orphan runner. This does not inspect or change any other service.
schtasks.exe /Query /TN "%TASK_NAME%" >nul 2>&1
if not errorlevel 1 (
    schtasks.exe /Change /TN "%TASK_NAME%" /DISABLE >"%TASK_OUTPUT%" 2>&1
    if errorlevel 1 exit /b 1
    schtasks.exe /End /TN "%TASK_NAME%" >"%TASK_OUTPUT%" 2>&1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try{$scriptPath=[IO.Path]::GetFullPath($env:MONITOR_SCRIPT); Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {$_.Name -ieq 'cmd.exe' -and $_.CommandLine -and $_.CommandLine.IndexOf($scriptPath,[StringComparison]::OrdinalIgnoreCase) -ge 0 -and $_.CommandLine -match '(?i)--run(?:\s|$)'} | ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue}; exit 0}catch{exit 1}"
if errorlevel 1 exit /b 1
REM Let Task Scheduler release the previous instance before the replacement task starts.
timeout /t 2 /nobreak >nul
exit /b 0


:CreateAndStartTask
schtasks.exe /Create /TN "%TASK_NAME%" /TR "\"%~f0\" --run" /SC ONSTART /DELAY 0001:00 /RU SYSTEM /RL HIGHEST /F >"%TASK_OUTPUT%" 2>&1
if errorlevel 1 exit /b 1

REM PT0S removes Task Scheduler's default 72-hour execution limit.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try{$settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -StartWhenAvailable; Set-ScheduledTask -TaskName $env:TASK_NAME -Settings $settings -ErrorAction Stop | Out-Null; exit 0}catch{exit 1}"
if errorlevel 1 exit /b 1

schtasks.exe /Change /TN "%TASK_NAME%" /ENABLE >"%TASK_OUTPUT%" 2>&1
if errorlevel 1 exit /b 1
schtasks.exe /Run /TN "%TASK_NAME%" >"%TASK_OUTPUT%" 2>&1
if errorlevel 1 exit /b 1
timeout /t 2 /nobreak >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try{if((Get-ScheduledTask -TaskName $env:TASK_NAME -ErrorAction Stop).State -eq 'Running'){exit 0}; exit 1}catch{exit 1}"
if errorlevel 1 exit /b 1
exit /b 0


:Shutdown
call :StopMonitor
if errorlevel 1 goto :Failed
call :StopApi
if errorlevel 1 goto :Failed
echo [SUCCESS] API monitor and InsoleApi are stopped. You can update InsoleApi now.
exit /b 0


:Uninstall
call :StopMonitor
if errorlevel 1 goto :Failed
schtasks.exe /Delete /TN "%TASK_NAME%" /F >nul 2>&1
echo [SUCCESS] API monitor task removed. InsoleApi was not changed.
exit /b 0


:Help
echo Usage:
echo   %~nx0                 Idempotently install/update and start the API monitor.
echo   %~nx0 --shutdown      Stop the API monitor and InsoleApi for an update.
echo   %~nx0 --uninstall     Remove the API monitor task only.
echo.
echo The monitor checks %HEALTH_URL% every 5 minutes and restarts only InsoleApi on failure.
exit /b 0


:Failed
echo [FAILED] Operation failed. Run this BAT as Administrator and review %LOG_PATH%.
if exist "%TASK_OUTPUT%" type "%TASK_OUTPUT%"
exit /b 1


:Log
>>"%LOG_PATH%" echo [%date% %time%] %~1
exit /b 0
