@echo off
cd /d "%~dp0"
set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
if exist "%~dp0ORBIT-WPF\publish\Orbit.exe" (
    start "" "%~dp0ORBIT-WPF\publish\Orbit.exe"
) else (
    start "" "%~dp0publish\Orbit.exe"
)
