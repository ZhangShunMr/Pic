$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
New-Item -ItemType Directory -Force docs/windows | Out-Null
function Run-Dotnet([string]$Log, [string[]]$Arguments) {
 & dotnet @Arguments 2>&1 | Tee-Object -FilePath $Log
 if ($LASTEXITCODE -ne 0) { throw "dotnet command failed: $Arguments" }
}
Run-Dotnet 'docs/windows/build-log.txt' @('build', 'TrimContact.sln', '-c', 'Release')
Run-Dotnet 'docs/windows/core-log.txt' @('run','--project','tests/TrimContact.Verification','-c','Release','--no-build','--','artifacts/windows-tests')
Copy-Item artifacts/windows-tests/docs/core-verification.json docs/windows/core-verification.json
Run-Dotnet 'docs/windows/workflow-log.txt' @('run','--project','tests/TrimContact.DesktopWorkflow','-c','Release','--no-build','--','artifacts/windows-tests')
Copy-Item artifacts/windows-tests/docs/desktop-workflow-verification.json docs/windows/desktop-workflow-verification.json
Run-Dotnet 'docs/windows/ui-log.txt' @('run','--project','tests/TrimContact.WindowsChecks','-c','Release','--no-build','--','docs/windows')
