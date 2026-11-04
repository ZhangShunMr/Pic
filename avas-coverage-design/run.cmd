@echo off
cd /d "%~dp0"
where dotnet >nul 2>nul
if errorlevel 1 (
  echo Please install .NET SDK 10.0.403, or use the self-contained Windows package.
  pause
  exit /b 1
)
dotnet run --project src\Avas.Desktop -c Release
if errorlevel 1 pause
