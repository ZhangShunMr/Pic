$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet build BlowerResonance.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project tests/Blower.Verification -c Release -- docs/core-evidence-windows.json
if ($LASTEXITCODE -ne 0) { throw 'Core verification failed' }
dotnet run --project tests/Blower.WorkflowVerification -c Release -- docs/workflow-evidence-windows.json
if ($LASTEXITCODE -ne 0) { throw 'Workflow verification failed' }
dotnet publish src/Blower.Desktop/Blower.Desktop.csproj -c Release -r win-x64 --self-contained true -o release/win-x64
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Write-Host 'Launch release/win-x64/BlowerResonance.exe. Windows UI checks remain manual; follow docs/Windows验收步骤.md.'
