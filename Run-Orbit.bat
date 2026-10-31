@echo off
cd /d "%~dp0"
set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
start "" "%~dp0publish\Orbit.exe"
