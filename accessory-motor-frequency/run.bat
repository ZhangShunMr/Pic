@echo off
cd /d "%~dp0"
dotnet run --project src\MotorFrequency.Desktop -c Release
if errorlevel 1 pause
