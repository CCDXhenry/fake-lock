@echo off
cd /d "%~dp0"
if not exist "FakeLock.exe" (
    call build.bat
)
start "" "FakeLock.exe" %*
