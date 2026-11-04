@echo off
cd /d "%~dp0"
dotnet run --project src\Avas.App -- %*
