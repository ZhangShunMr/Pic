@echo off
cd /d "%~dp0"
dotnet run --project src\Blower.App\Blower.App.csproj --urls http://127.0.0.1:5178
pause
