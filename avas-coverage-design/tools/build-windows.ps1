$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    dotnet build AvasCoverage.sln -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet run --no-build --project tests/Avas.Verification -c Release -- docs/core-verification.json
    if ($LASTEXITCODE -ne 0) { throw 'Core verification failed' }
    dotnet run --no-build --project tests/Avas.WorkspaceVerification -c Release -- docs/workspace-verification.json
    if ($LASTEXITCODE -ne 0) { throw 'Workspace verification failed' }
    dotnet run --no-build --project tests/Avas.DesktopVerification -c Release -- docs/windows-ui
    if ($LASTEXITCODE -ne 0) { throw 'Desktop verification failed; inspect docs/windows-ui/desktop-evidence.json' }
    dotnet publish src/Avas.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/AvasCoverage-win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item README.md artifacts/AvasCoverage-win-x64/README.md -Force
    Compress-Archive -Path artifacts/AvasCoverage-win-x64/* -DestinationPath artifacts/AvasCoverage-win-x64.zip -Force
    Write-Host 'Windows package: artifacts/AvasCoverage-win-x64.zip'
} finally { Pop-Location }
