$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
& dotnet publish src/TrimContact.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/win-x64 2>&1 | Tee-Object docs/windows/publish-log.txt
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed' }
$program = Join-Path $PWD 'artifacts/win-x64/TrimContact.Desktop.exe'
$process = Start-Process -FilePath $program -WorkingDirectory (Split-Path $program) -PassThru
try {
 $ready = $false
 for ($i=0; $i -lt 80; $i++) {
  Start-Sleep -Milliseconds 250
  $process.Refresh()
  if ($process.HasExited) { throw "Published program exited early: $($process.ExitCode)" }
  if ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -like '*内饰卡扣*') { $ready=$true; break }
 }
 if (-not $ready) { throw 'Published program did not display its native window' }
 $record = [ordered]@{
  executedUtc=[DateTimeOffset]::UtcNow.ToString('o'); sourceCommit=$env:GITHUB_SHA
  runUrl="https://github.com/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
  executable='artifacts/win-x64/TrimContact.Desktop.exe'; sha256=(Get-FileHash $program -Algorithm SHA256).Hash
  platform=[Environment]::OSVersion.ToString(); actualProcessStarted=$true
  nativeWindowTitle=$process.MainWindowTitle; frameworkDependentRuntimeRequired=$false
  note='Actual self-contained executable started and its native window observed. Not a certification of Windows 10/11, DPI, shell-dialog, installer or antivirus compatibility.'
 }
 & ./tools/capture-windows.ps1 -Name '00-published-exe'
 Move-Item docs/screenshots/00-published-exe.png docs/windows/screenshots/00-published-exe.png -Force
 [void]$process.CloseMainWindow()
 if (-not $process.WaitForExit(10000)) { throw 'Published program did not close normally' }
 if ($process.ExitCode -ne 0) { throw "Program exit code: $($process.ExitCode)" }
 $record.normalExitCode=$process.ExitCode
 $record | ConvertTo-Json | Set-Content docs/windows/published-exe-evidence.json -Encoding utf8
 Write-Host 'PASS published native self-contained executable opened and closed normally'
} finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id } }
