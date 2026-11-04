@echo off
cd /d "%~dp0"
dotnet run --project src/TrimContact.App -c Release --no-launch-profile -- %*
