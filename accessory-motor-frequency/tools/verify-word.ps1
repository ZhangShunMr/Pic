$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
New-Item docs/windows-evidence -ItemType Directory -Force | Out-Null
$record = [ordered]@{
    executedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    platform = [Environment]::OSVersion.ToString()
    sourceCommit = $env:GITHUB_SHA
    engine = 'Microsoft Word COM'
    available = $false
    actualPages = $null
    finalLayoutInspected = $false
}
$type = [type]::GetTypeFromProgID('Word.Application')
if ($null -eq $type) {
    $record.reason = 'Microsoft Word COM is not registered on this Windows runner. No Word page count or PDF claimed.'
} else {
    $word = $null
    $document = $null
    try {
        $word = New-Object -ComObject Word.Application
        $word.Visible = $false
        $word.DisplayAlerts = 0
        $document = $word.Documents.Open((Join-Path $PWD 'docs/操作说明书-草稿.docx'), $false, $true)
        $document.Repaginate()
        $record.available = $true
        $record.actualPages = $document.ComputeStatistics(2)
        $document.ExportAsFixedFormat((Join-Path $PWD 'docs/windows-evidence/操作说明书-Word预览.pdf'), 17)
        $record.note = 'Actual Word page count and PDF only; human per-page layout inspection still required.'
    } catch {
        $record.reason = $_.Exception.Message
    } finally {
        if ($null -ne $document) { $document.Close($false) }
        if ($null -ne $word) { $word.Quit() }
    }
}
$record | ConvertTo-Json | Set-Content docs/windows-evidence/word-pagination.json -Encoding utf8
Write-Host ($record | ConvertTo-Json -Compress)
