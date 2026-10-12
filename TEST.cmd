@echo off
setlocal
cd /d "%~dp0"
call BUILD.cmd
if errorlevel 1 exit /b 1
ProvolverBridge.exe --self-test
exit /b %errorlevel%
