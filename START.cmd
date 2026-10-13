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
echo 5 - Test charge-up rumble (no kick)
choice /c 12345 /n /m "Choose 1, 2, 3, 4 or 5: "
if errorlevel 5 goto charge
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
:charge
ProvolverBridge.exe --charge-test
goto menu
