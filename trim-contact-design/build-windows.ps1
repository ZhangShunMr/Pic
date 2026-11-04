$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet command failed: $args" }
}
Invoke-Dotnet build TrimContact.sln -c Release
Invoke-Dotnet run --project tests/TrimContact.Verification -c Release --no-build -- .
Invoke-Dotnet run --project tests/TrimContact.DesktopWorkflow -c Release --no-build -- .
Invoke-Dotnet publish src/TrimContact.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/win-x64
Write-Host "Built: artifacts/win-x64/TrimContact.Desktop.exe (Windows UI still requires manual verification)."
