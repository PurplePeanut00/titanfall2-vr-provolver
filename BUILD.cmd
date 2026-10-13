@echo off
setlocal
cd /d "%~dp0"
set "TF2CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%TF2CSC%" (
 echo ERROR: The 64-bit .NET Framework compiler was not found.
 echo Install or repair .NET Framework 4.8, then try again.
 exit /b 1
)
"%TF2CSC%" /nologo /target:exe /platform:x64 /optimize+ /out:ProvolverBridge.exe Bridge.cs
exit /b %errorlevel%
