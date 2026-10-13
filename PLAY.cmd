@echo off
setlocal
cd /d "%~dp0"
title Titanfall 2 VR - ProVolver
if not exist "%~dp0ProvolverBridge.exe" (
 echo Run INSTALL.cmd from the downloaded package first.
 pause
 exit /b 1
)
echo Turn on your ProVolver and close ProTube Companion.
echo Launch Titanfall 2 VR normally and load your campaign.
echo Keep this window open while playing. Ctrl+C stops recoil.
echo.
"%~dp0ProvolverBridge.exe" --bridge
if errorlevel 1 pause
