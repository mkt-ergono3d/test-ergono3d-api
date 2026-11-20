@echo off
setlocal
if /i not "%~1"=="--run" exit /b 2
set /p "signal="
if /i not "%signal%"=="start" exit /b 3
if not defined RHINO_TOKEN exit /b 4
if not defined RHINO_COMPUTE_KEY exit /b 5
set "ASPNETCORE_URLS=http://127.0.0.1:16500"
"%~dp0rhino.compute\rhino.compute.exe" --port 16500 --childcount 1 --idlespan 90 --spawn-on-startup >>"%~dp0rhino-runtime.log" 2>&1
