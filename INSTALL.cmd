@echo off
setlocal
cd /d "%~dp0"
title Titanfall 2 VR - ProVolver Setup
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0tools\Install.ps1"
if errorlevel 1 (
 echo.
 echo Setup did not finish. Read the error above before retrying.
)
echo.
pause
