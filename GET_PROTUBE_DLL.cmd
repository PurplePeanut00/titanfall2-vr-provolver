@echo off
setlocal
cd /d "%~dp0"
echo Downloading the tested x64 DLL directly from ProTubeVR's official repository.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Get-ProTubeDll.ps1"
set "TF2RESULT=%errorlevel%"
if not "%TF2RESULT%"=="0" echo Setup failed. The README includes manual download instructions.
pause
exit /b %TF2RESULT%
