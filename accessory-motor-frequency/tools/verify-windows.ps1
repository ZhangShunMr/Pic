param([string]$Output = "docs/windows-evidence")
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)
if (-not $IsWindows -and $PSVersionTable.PSVersion.Major -ge 6) { throw "Run on Windows with an interactive desktop." }
dotnet build MotorFrequency.sln -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
dotnet run --project tests/MotorFrequency.Verification -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Core verification failed" }
dotnet run --project tests/MotorFrequency.Application.Verification -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Application verification failed" }
dotnet run --project tests/MotorFrequency.Desktop.Verification -c Release --no-build -- $Output
if ($LASTEXITCODE -ne 0) { throw "WPF verification failed; inspect output" }
Write-Host "Inspect every captured real WPF image and complete the manual Windows checklist before acceptance."
