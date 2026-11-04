param([ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet publish src/Cabin.Desktop/Cabin.Desktop.csproj -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o "artifacts/$Runtime"
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item README.md "artifacts/$Runtime/README.md"
Copy-Item examples "artifacts/$Runtime/examples" -Recurse -Force
Write-Host "Offline native application: artifacts/$Runtime/CabinAcoustics.exe. Copy the entire folder, not just the exe."
