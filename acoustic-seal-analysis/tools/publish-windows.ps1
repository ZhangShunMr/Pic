$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet build AcousticSeal.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project tests/AcousticSeal.Verification -c Release
if ($LASTEXITCODE -ne 0) { throw 'Verification failed' }
dotnet publish src/AcousticSeal.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/win-x64
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Compress-Archive -Path artifacts/win-x64/* -DestinationPath artifacts/AcousticSeal-V3.0-win-x64.zip -Force
Write-Host 'Complete release: artifacts/AcousticSeal-V3.0-win-x64.zip'
