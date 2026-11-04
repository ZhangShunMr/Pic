@echo off
cd /d "%~dp0"
dotnet run --project src\MotorFrequency.App -- %*
