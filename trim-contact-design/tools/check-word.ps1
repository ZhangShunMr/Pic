$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
New-Item -ItemType Directory -Force docs/windows | Out-Null
$record = [ordered]@{ executedUtc=[DateTimeOffset]::UtcNow.ToString('o'); sourceCommit=$env:GITHUB_SHA; wordAvailable=$false; actualWordPageCount=$null; actualWordPdfPageCount=$null; verified=$false }
$word=$null
$document=$null
try {
 $type=[Type]::GetTypeFromProgID('Word.Application')
 if ($null -eq $type) { throw 'Microsoft Word COM application is not installed on this Windows runner.' }
 $word=New-Object -ComObject Word.Application
 $word.Visible=$false
 $word.DisplayAlerts=0
 $record.wordAvailable=$true
 $record.wordVersion=$word.Version
 $manual=(Resolve-Path 'docs/操作说明书-待Windows核验.docx').Path
 $document=$word.Documents.Open($manual,$false,$true)
 $document.Repaginate()
 $record.actualWordPageCount=$document.ComputeStatistics(2)
 $pdf=Join-Path $PWD 'docs/windows/操作说明书-Word预览.pdf'
 $document.ExportAsFixedFormat($pdf,17)
 $record.wordPdfExported=$true
 $record.note='Word pagination/export executed; human per-page layout inspection still required.'
} catch { $record.reason=$_.Exception.Message }
finally {
 if ($null -ne $document) { $document.Close(0) }
 if ($null -ne $word) { $word.Quit() }
 $record | ConvertTo-Json | Set-Content docs/windows/word-pagination.json -Encoding utf8
}
Get-Content docs/windows/word-pagination.json
