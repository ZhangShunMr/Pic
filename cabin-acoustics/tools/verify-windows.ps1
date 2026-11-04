$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
dotnet build CabinAcoustics.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet run --project tests/Cabin.Tests -c Release -- docs/test-windows.json examples
if ($LASTEXITCODE -ne 0) { throw 'Core and MVVM verification failed' }
dotnet run --project tests/Cabin.WindowsChecks -c Release -- docs/screenshots
if ($LASTEXITCODE -ne 0) { throw 'Native Windows UI checks failed. Inspect docs/screenshots/windows-ui-evidence.json' }
Write-Host 'Actual WPF window captures and Windows integration evidence are in docs/screenshots. Inspect every PNG before using in the manual.'
