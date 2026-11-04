@echo off
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Please install .NET SDK 10.0.403, or use the complete self-contained release folder.
  pause
  exit /b 1
)
dotnet run --project src\AcousticSeal.Desktop -c Release
if errorlevel 1 pause
