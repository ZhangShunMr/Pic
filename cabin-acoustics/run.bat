@echo off
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo Install .NET 8 SDK to build this source checkout. For offline running use the self-contained Windows package.
  pause
  exit /b 1
)
dotnet run --project src\Cabin.Desktop\Cabin.Desktop.csproj -c Release
if errorlevel 1 pause
