param([string]$Output = "docs/windows-evidence")
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
if (-not $IsWindows -and $PSVersionTable.PSVersion.Major -ge 6) { throw "Run on Windows with an interactive desktop." }
New-Item $Output -ItemType Directory -Force | Out-Null
dotnet build MotorFrequency.sln -c Release | Tee-Object -FilePath (Join-Path $Output 'build-log.txt')
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
dotnet run --project tests/MotorFrequency.Verification -c Release --no-build | Tee-Object -FilePath (Join-Path $Output 'core-log.txt')
if ($LASTEXITCODE -ne 0) { throw "Core verification failed" }
Copy-Item docs/core-evidence.json (Join-Path $Output 'core-evidence.json') -Force
dotnet run --project tests/MotorFrequency.Application.Verification -c Release --no-build | Tee-Object -FilePath (Join-Path $Output 'application-log.txt')
if ($LASTEXITCODE -ne 0) { throw "Application verification failed" }
Copy-Item docs/application-evidence.json (Join-Path $Output 'application-evidence.json') -Force
dotnet run --project tests/MotorFrequency.Desktop.Verification -c Release --no-build -- $Output | Tee-Object -FilePath (Join-Path $Output 'wpf-log.txt')
if ($LASTEXITCODE -ne 0) { throw "WPF verification failed; inspect output" }
Write-Host "Inspect every captured real WPF image and complete the manual Windows checklist before acceptance."
