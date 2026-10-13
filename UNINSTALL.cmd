@echo off
setlocal
set "PVREMOVE_SCRIPT=%~dp0tools\Uninstall.ps1"
cd /d "%TEMP%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PVREMOVE_SCRIPT%"
pause
