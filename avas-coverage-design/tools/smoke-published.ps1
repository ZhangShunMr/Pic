$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class AvasWindowCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr hdc, uint flags);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
}
'@
$executable = (Resolve-Path artifacts/AvasCoverage-win-x64/AvasCoverage.exe).Path
$process = Start-Process -FilePath $executable -PassThru
try {
    for ($attempt=0; $attempt -lt 40; $attempt++) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) { throw "Published program exited before showing a window: $($process.ExitCode)" }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
    }
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Published executable did not show a native window' }
    Start-Sleep -Milliseconds 500
    [AvasWindowCapture]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    $rectangle = New-Object AvasWindowCapture+Rect
    if (-not [AvasWindowCapture]::GetWindowRect($process.MainWindowHandle, [ref]$rectangle)) { throw 'Cannot read real window bounds' }
    $width=$rectangle.Right-$rectangle.Left; $height=$rectangle.Bottom-$rectangle.Top
    $bitmap = New-Object System.Drawing.Bitmap($width,$height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    try {
        if (-not [AvasWindowCapture]::PrintWindow($process.MainWindowHandle,$hdc,2)) { throw 'Windows PrintWindow capture failed' }
    } finally { $graphics.ReleaseHdc($hdc) }
    $image = [IO.Path]::GetFullPath('docs/windows-ui/12-published-native-window.png')
    $bitmap.Save($image,[System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose(); $bitmap.Dispose()
    $pidValue=$process.Id; $title=$process.MainWindowTitle
    if (-not $process.CloseMainWindow()) { throw 'Published window did not accept close request' }
    if (-not $process.WaitForExit(15000)) { throw 'Published native program did not close in 15 seconds' }
    if ($process.ExitCode -ne 0) { throw "Published program exit code $($process.ExitCode)" }
    @{status='passed';at=(Get-Date).ToUniversalTime().ToString('o');os=[Environment]::OSVersion.VersionString;process_id=$pidValue;title=$title;exit_code=$process.ExitCode;executable_sha256=(Get-FileHash $executable -Algorithm SHA256).Hash.ToLowerInvariant();screenshot='12-published-native-window.png';screenshot_sha256=(Get-FileHash $image -Algorithm SHA256).Hash.ToLowerInvariant();capture='user32 PrintWindow of directly launched self-contained executable';width=$width;height=$height} | ConvertTo-Json | Set-Content docs/windows-ui/published-executable.json -Encoding utf8
    Write-Host 'PASS published self-contained executable showed and closed its real native window'
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
