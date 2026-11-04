$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
dotnet build BlowerResonance.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project tests/Blower.Verification -c Release -- docs/core-evidence-windows.json
if ($LASTEXITCODE -ne 0) { throw 'Core verification failed' }
dotnet run --project tests/Blower.WorkflowVerification -c Release -- docs/workflow-evidence-windows.json
if ($LASTEXITCODE -ne 0) { throw 'Workflow verification failed' }
dotnet run --project tests/Blower.WindowsChecks -c Release -- docs/screenshots
if ($LASTEXITCODE -ne 0) { throw 'Native Windows UI verification failed' }
dotnet publish src/Blower.Desktop/Blower.Desktop.csproj -c Release -r win-x64 --self-contained true -o release/win-x64
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
./tools/smoke-published.ps1
Write-Host 'Actual WPF and published exe checks completed. Target hardware, native file-dialog interaction, high DPI and Word pagination remain subject to docs/Windows验收步骤.md.'
