$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet publish src/MotorFrequency.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/win-x64
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
Copy-Item README.md artifacts/win-x64/README.md -Force
Copy-Item examples artifacts/win-x64/examples -Recurse -Force
Copy-Item docs artifacts/win-x64/docs -Recurse -Force
Get-ChildItem artifacts/win-x64 -Recurse -Filter *.pdb | Remove-Item -Force
New-Item release -ItemType Directory -Force | Out-Null
Compress-Archive -Path artifacts/win-x64/* -DestinationPath release/MotorFrequency-V2.0-win-x64.zip -Force
Get-FileHash release/MotorFrequency-V2.0-win-x64.zip -Algorithm SHA256 | Format-List
Write-Host "Extract every file, then run MotorFrequency.exe on Windows. This publish script does not validate WPF execution."
