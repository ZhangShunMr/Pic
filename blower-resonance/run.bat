@echo off
setlocal
cd /d "%~dp0"
if exist "release\win-x64\BlowerResonance.exe" (
  start "" "release\win-x64\BlowerResonance.exe"
  exit /b 0
)
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Install .NET 8 SDK or extract the win-x64 portable package first.
  pause
  exit /b 1
)
dotnet run --project src\Blower.Desktop\Blower.Desktop.csproj -c Release
if errorlevel 1 pause
