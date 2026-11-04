$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$program = Join-Path $PWD 'artifacts/win-x64/MotorFrequency.exe'
if (-not (Test-Path $program)) { throw 'Published self-contained executable is missing' }
$process = Start-Process -FilePath $program -WorkingDirectory (Split-Path $program) -PassThru
try {
    $ready = $false
    for ($index = 0; $index -lt 60; $index++) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
        if ($process.HasExited) { throw "Published process exited before window creation: $($process.ExitCode)" }
        if ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -like '*汽车附件电机电磁激励频率设计软件*') {
            $ready = $true
            break
        }
    }
    if (-not $ready) { throw 'Published executable did not create the expected native window within 15 seconds' }
    $record = [ordered]@{
        executedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        executable = 'artifacts/win-x64/MotorFrequency.exe'
        sha256 = (Get-FileHash $program -Algorithm SHA256).Hash
        sourceCommit = $env:GITHUB_SHA
        platform = [Environment]::OSVersion.ToString()
        actualProcessStarted = $true
        nativeWindowTitle = $process.MainWindowTitle
        frameworkDependentRuntimeRequired = $false
        limitation = 'Runner OS startup/normal close only; not Windows 10/11 hardware, offline network isolation, DPI or installer certification.'
    }
    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(10000)) { throw 'Published native window did not close normally' }
    if ($process.ExitCode -ne 0) { throw "Published exit code $($process.ExitCode)" }
    $record.normalExitCode = $process.ExitCode
    $record | ConvertTo-Json | Set-Content 'docs/windows-evidence/published-exe.json' -Encoding utf8
    Write-Host 'PASS actual self-contained EXE created expected native window and exited normally'
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
}
