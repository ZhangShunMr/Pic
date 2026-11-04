$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class BlowerNative {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
}
'@
$exe = Join-Path $PWD 'release/win-x64/BlowerResonance.exe'
$process = Start-Process $exe -WorkingDirectory (Split-Path $exe) -PassThru
try {
 $ready = $false
 for ($index=0; $index -lt 60; $index++) {
  Start-Sleep -Milliseconds 250
  $process.Refresh()
  if ($process.HasExited) { throw "Published exe exited: $($process.ExitCode)" }
  if ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -like '*鼓风机*') { $ready=$true; break }
 }
 if (-not $ready) { throw 'Published exe did not show its native window' }
 $title = $process.MainWindowTitle
 [void]$process.CloseMainWindow()
 # Ordinary blank startup is an unsaved project; discard using the actual native Yes/No/Cancel dialog.
 for ($index=0; $index -lt 40; $index++) {
  if ($process.HasExited) { break }
  $dialog = [BlowerNative]::FindWindow('#32770','空调鼓风机风量挡位与共振避让设计软件')
  if ($dialog -ne [IntPtr]::Zero) { [void][BlowerNative]::PostMessage($dialog,0x111,[IntPtr]7,[IntPtr]::Zero) }
  Start-Sleep -Milliseconds 250
 }
 if (-not $process.WaitForExit(1000)) { throw 'Published exe did not close after native discard dialog' }
 if ($process.ExitCode -ne 0) { throw "Published exe exit code $($process.ExitCode)" }
 @{
  executedUtc=[DateTimeOffset]::UtcNow.ToString('o'); sourceCommit=$env:GITHUB_SHA
  platform=[Environment]::OSVersion.ToString(); executable='release/win-x64/BlowerResonance.exe'
  sha256=(Get-FileHash $exe -Algorithm SHA256).Hash; nativeWindowTitle=$title
  actualProcessStarted=$true; normalExitCode=$process.ExitCode
  note='Self-contained exe started directly; actual native window observed and native unsaved dialog discarded. Not Windows 10/11 hardware or high-DPI certification.'
 } | ConvertTo-Json | Set-Content docs/screenshots/published-exe-evidence.json -Encoding utf8
 Write-Host 'PASS published self-contained exe opened and closed native window'
} finally {
 if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
