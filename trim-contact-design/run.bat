@echo off
cd /d "%~dp0"
if exist "artifacts\win-x64\TrimContact.Desktop.exe" (
 start "" "artifacts\win-x64\TrimContact.Desktop.exe"
 exit /b 0
)
dotnet run --project src\TrimContact.Desktop -c Release
if errorlevel 1 pause
