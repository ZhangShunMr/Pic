param(
  [string]$Output = 'docs/screenshots/native-window.png'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeCapture {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
}
'@
$processes = @(Get-Process -Name AcousticSeal.Desktop -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 })
if ($processes.Count -ne 1) { throw 'Open exactly one actual AcousticSeal.Desktop window before capture.' }
$p = $processes[0]
[NativeCapture]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
[NativeCapture]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 600
$r = New-Object NativeCapture+RECT
if (-not [NativeCapture]::GetWindowRect($p.MainWindowHandle, [ref]$r)) { throw 'Cannot read native window rectangle.' }
$w = $r.Right - $r.Left
$h = $r.Bottom - $r.Top
if ($w -lt 500 -or $h -lt 400) { throw 'Window is not visible at a usable size.' }
$full = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full)) | Out-Null
$bitmap = New-Object Drawing.Bitmap($w, $h)
$graphics = [Drawing.Graphics]::FromImage($bitmap)
try {
  $graphics.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object Drawing.Size($w, $h)))
  $bitmap.Save($full, [Drawing.Imaging.ImageFormat]::Png)
} finally { $graphics.Dispose(); $bitmap.Dispose() }
@{
  captured_at_utc = [DateTime]::UtcNow.ToString('o')
  process_id = $p.Id
  window_title = $p.MainWindowTitle
  width = $w
  height = $h
  screenshot = $full
  method = 'Actual visible native window pixels via CopyFromScreen; inspect for occlusion and correctness.'
} | ConvertTo-Json | Set-Content ($full + '.json') -Encoding UTF8
Write-Host "Captured actual desktop pixels: $full. Inspect the image; a captured file alone does not prove UI acceptance."
