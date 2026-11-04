param([string]$Executable = "$PSScriptRoot/../release/win-x64/BlowerResonance.exe")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CaptureWindow {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
}
'@
$process = Start-Process -FilePath (Resolve-Path $Executable) -PassThru
if (-not $process.WaitForInputIdle(15000)) { throw 'Window did not become idle' }
$destination = Join-Path $PSScriptRoot '../docs/screenshots'
New-Item -ItemType Directory -Force $destination | Out-Null
$scenes = @('01-空白项目','02-模拟示例-项目来源','03-模态属性','04-挡位与约束','05-原生频率图','06-扫描与比较','07-输入错误','08-保存重开')
foreach ($scene in $scenes) {
 Read-Host "在真实程序中完成 $scene 对应操作，确保无遮挡，然后回到此处按回车"
 $process.Refresh()
 if ($process.HasExited -or $process.MainWindowHandle -eq 0) { throw 'Application window missing' }
 $windowHandle = $process.MainWindowHandle
 if ([CaptureWindow]::IsIconic($windowHandle)) { throw 'Restore the application window before capture' }
 [CaptureWindow]::SetForegroundWindow($windowHandle) | Out-Null
 Start-Sleep -Milliseconds 700
 $bounds = New-Object CaptureWindow+Rect
 if (-not [CaptureWindow]::GetWindowRect($windowHandle, [ref]$bounds)) { throw 'Cannot read window bounds' }
 $width = $bounds.Right - $bounds.Left
 $height = $bounds.Bottom - $bounds.Top
 $bitmap = New-Object System.Drawing.Bitmap $width,$height
 $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
 try {
  $graphics.CopyFromScreen($bounds.Left,$bounds.Top,0,0,$bitmap.Size)
  $path = Join-Path $destination "$scene.png"
  $bitmap.Save($path,[System.Drawing.Imaging.ImageFormat]::Png)
  Write-Host "真实桌面窗口已捕获：$path；请人工核对无遮挡、清晰度及内容。"
 } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
