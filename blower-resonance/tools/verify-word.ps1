param([string]$Document = "$PSScriptRoot/../docs/操作说明书-待验收稿.docx")
$ErrorActionPreference = 'Stop'
$recordPath = Join-Path $PSScriptRoot '../docs/word-pagination.json'
$record = [ordered]@{
 executedUtc=[DateTimeOffset]::UtcNow.ToString('o')
 document=[System.IO.Path]::GetFullPath($Document)
 microsoftWordInstalled=$null -ne [type]::GetTypeFromProgID('Word.Application')
 actualWordPageCount=$null
 humanPageReview='pending; inspect every exported page for clipping, captions, tables and page breaks'
}
if (-not $record.microsoftWordInstalled) {
 $record.status='not executed: Microsoft Word is not installed'
 $record | ConvertTo-Json | Set-Content $recordPath -Encoding utf8
 throw 'Microsoft Word is not installed. No actual Word pagination or PDF is claimed.'
}
$word = $null
$doc = $null
try {
 $word = New-Object -ComObject Word.Application
 $word.Visible = $false
 $word.DisplayAlerts = 0
 $doc = $word.Documents.Open($record.document,$false,$true)
 $doc.Repaginate()
 $pages = $doc.ComputeStatistics(2)
 $record.actualWordPageCount=$pages
 $record.wordVersion=$word.Version
 $record.documentSha256=(Get-FileHash $record.document -Algorithm SHA256).Hash
 if ($pages -lt 10) { throw "Actual Microsoft Word page count is $pages, below 10. Revise valid content and layout before submission." }
 $pdf = [System.IO.Path]::ChangeExtension($record.document,'.pdf')
 $doc.ExportAsFixedFormat($pdf,17)
 $record.pdf=$pdf
 $record.pdfSha256=(Get-FileHash $pdf -Algorithm SHA256).Hash
 $record.status='Word repagination and PDF export executed; human page-by-page review still required'
} catch {
 $record.error=$_.Exception.Message
 throw
} finally {
 if ($null -ne $doc) { $doc.Close(0); [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($doc) }
 if ($null -ne $word) { $word.Quit(); [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) }
 $record | ConvertTo-Json | Set-Content $recordPath -Encoding utf8
}
