@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo 启动后请打开 http://127.0.0.1:8786
if exist Cabin.App.dll (
 dotnet Cabin.App.dll --port 8786 --data-directory "%CD%\Data"
) else (
 dotnet run --project src\Cabin.App\Cabin.App.csproj -- --port 8786 --data-directory "%CD%\Data"
)
pause
