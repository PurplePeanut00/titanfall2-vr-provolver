@echo off
setlocal
cd /d "%~dp0"
title Titanfall 2 VR - ProVolver Bridge
echo Building ProVolver Bridge...
call BUILD.cmd
if errorlevel 1 (
 echo Build failed. Copy the errors above into a GitHub issue.
 pause
 exit /b 1
)
:menu
echo.
echo 1 - Test ONE full-power ProVolver kick
echo 2 - Start Titanfall 2 live recoil bridge
echo 3 - Monitor shots only (no ProVolver kicks)
echo 4 - Exit
choice /c 1234 /n /m "Choose 1, 2, 3 or 4: "
if errorlevel 4 exit /b 0
if errorlevel 3 goto monitor
if errorlevel 2 goto bridge
ProvolverBridge.exe --test
goto menu
:bridge
ProvolverBridge.exe --bridge
goto menu
:monitor
ProvolverBridge.exe --monitor
goto menu
