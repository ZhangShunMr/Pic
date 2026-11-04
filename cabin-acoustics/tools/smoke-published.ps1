$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$program = Join-Path $PWD 'artifacts/win-x64/CabinAcoustics.exe'
if (-not (Test-Path $program)) { throw 'Self-contained executable is missing' }
$process = Start-Process -FilePath $program -WorkingDirectory (Split-Path $program) -PassThru
try {
  $ready = $false
  for ($index = 0; $index -lt 40; $index++) {
    Start-Sleep -Milliseconds 250
    $process.Refresh()
    if ($process.HasExited) { throw "Published application exited before showing a window: $($process.ExitCode)" }
    if ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -like '*座舱模态工作台*') {
      $ready = $true
      break
    }
  }
  if (-not $ready) { throw 'Published executable did not show the expected native window within 10 seconds' }
  $record = [ordered]@{
    executedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    executable = 'artifacts/win-x64/CabinAcoustics.exe'
    sha256 = (Get-FileHash $program -Algorithm SHA256).Hash
    sourceCommit = $env:GITHUB_SHA
    platform = [Environment]::OSVersion.ToString()
    actualProcessStarted = $true
    nativeWindowTitle = $process.MainWindowTitle
    frameworkDependentRuntimeRequired = $false
    note = 'Started published self-contained exe directly, observed its native window, and closed normally. Not a Windows 10/11 hardware, DPI, installer or antivirus compatibility certification.'
  }
  [void]$process.CloseMainWindow()
  if (-not $process.WaitForExit(10000)) { throw 'Published application did not close normally' }
  if ($process.ExitCode -ne 0) { throw "Published application exit code $($process.ExitCode)" }
  $record.normalExitCode = $process.ExitCode
  $record | ConvertTo-Json | Set-Content 'docs/screenshots/published-exe-evidence.json' -Encoding utf8
  Write-Host 'PASS published self-contained executable opened and closed its actual native window'
}
finally {
  if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
