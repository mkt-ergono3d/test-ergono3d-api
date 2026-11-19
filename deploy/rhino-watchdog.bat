@echo off
setlocal EnableExtensions DisableDelayedExpansion

if /i not "%~1"=="--run" (
    echo InsoleApi starts this launcher automatically when it receives an STL job.
    echo Set RhinoCompute__LauncherPath to the full path of this file for InsoleApi.
    exit /b 0
)

rem Wait until InsoleApi has added this process to its Windows Job Object.
set "INSOLE_RHINO_START="
set /p "INSOLE_RHINO_START="
if not "%INSOLE_RHINO_START%"=="start" exit /b 2
pushd "%~dp0" || exit /b 2

if not exist "%~dp0rhino.compute\rhino.compute.exe" (
    >"%~dp0rhino-runtime.log" echo Rhino.Compute executable was not found.
    popd
    exit /b 2
)

rem Keep Rhino in the foreground so InsoleApi can observe its exit.
"%~dp0rhino.compute\rhino.compute.exe" --port 6500 --childcount 1 --idlespan 0 --spawn-on-startup >"%~dp0rhino-runtime.log" 2>&1
set "RHINO_EXIT=%errorlevel%"
popd
exit /b %RHINO_EXIT%
