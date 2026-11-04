$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$program = Join-Path $PWD 'artifacts/win-x64/AcousticSeal.Desktop.exe'
$process = Start-Process -FilePath $program -WorkingDirectory (Split-Path $program) -PassThru
try {
  $ready = $false
  for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 250
    $process.Refresh()
    if ($process.HasExited) { throw "Published exe exited before native window: $($process.ExitCode)" }
    if ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -like '*V3.0*') { $ready = $true; break }
  }
  if (-not $ready) { throw 'Published exe did not show its expected native window within 15 seconds' }
  $record = [ordered]@{
    executedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    sourceCommit = $env:GITHUB_SHA
    platform = [Environment]::OSVersion.ToString()
    executable = 'artifacts/win-x64/AcousticSeal.Desktop.exe'
    sha256 = (Get-FileHash $program -Algorithm SHA256).Hash
    actualProcessStarted = $true
    nativeWindowTitle = $process.MainWindowTitle
    selfContained = $true
    note = 'Directly started actual published self-contained native executable and observed window. No Windows 10/11 hardware or all DPI compatibility claim.'
  }
  [void]$process.CloseMainWindow()
  if (-not $process.WaitForExit(10000)) { throw 'Published exe did not close normally' }
  if ($process.ExitCode -ne 0) { throw "Published exe exit code $($process.ExitCode)" }
  $record.normalExitCode = $process.ExitCode
  $record | ConvertTo-Json | Set-Content docs/screenshots/published-exe-evidence.json -Encoding utf8
} finally { if (-not $process.HasExited) { Stop-Process -Id $process.Id } }
