@echo off
cd /d "%~dp0"
if not exist PeripheralDesk.exe call Build.cmd
if not exist PeripheralDesk.exe (
  pause
  exit /b 1
)
start "" "%~dp0PeripheralDesk.exe" %*
