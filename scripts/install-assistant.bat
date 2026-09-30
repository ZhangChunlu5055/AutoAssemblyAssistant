@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-assistant.ps1"
if errorlevel 1 (
    echo Installation was not completed.
)
pause
