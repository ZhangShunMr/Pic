$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
New-Item -ItemType Directory -Force docs/screenshots | Out-Null
dotnet build AcousticSeal.sln -c Release
if ($LASTEXITCODE -ne 0) { throw 'Solution build failed' }
dotnet run --project tests/AcousticSeal.Verification -c Release *>&1 | Tee-Object docs/verification-windows.log
if ($LASTEXITCODE -ne 0) { throw 'Core or MVVM checks failed' }
dotnet run --project tests/AcousticSeal.WindowsChecks -c Release -- docs/screenshots
if ($LASTEXITCODE -ne 0) { throw 'Actual WPF checks failed; inspect windows-ui-evidence.json and binding-errors.log' }
