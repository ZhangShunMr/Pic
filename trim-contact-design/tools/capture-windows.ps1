param([Parameter(Mandatory=$true)][string]$Name)
$ErrorActionPreference = "Stop"
if ($env:OS -ne "Windows_NT") { throw "Requires a real Windows desktop and the running WPF program." }
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TrimCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
'@
$process = Get-Process -Name "TrimContact.Desktop" | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
if (-not $process) { throw "Open the actual TrimContact.Desktop.exe and navigate to the page first." }
$rect = New-Object TrimCapture+Rect
if (-not [TrimCapture]::GetWindowRect($process.MainWindowHandle, [ref]$rect)) { throw "Cannot read window bounds." }
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -lt 100 -or $height -lt 100) { throw "Window is minimized or has invalid bounds." }
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$dc = $graphics.GetHdc()
try {
 if (-not [TrimCapture]::PrintWindow($process.MainWindowHandle, $dc, 2)) { throw "Native window capture failed; no substitute image was created." }
} finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
$destination = Join-Path $PSScriptRoot "../docs/screenshots"
New-Item -ItemType Directory -Force $destination | Out-Null
$safeName = [IO.Path]::GetFileNameWithoutExtension($Name)
$path = Join-Path $destination ($safeName + ".png")
try { $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
Get-FileHash $path -Algorithm SHA256
Write-Host "Inspect this actual Windows capture for missing/black content and readability before adding it to the manual. This helper has not been run in Linux."
