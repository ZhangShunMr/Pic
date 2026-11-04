$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$record = [ordered]@{ executedUtc = [DateTimeOffset]::UtcNow.ToString('o'); wordAvailable = $false; wordPageCount = $null; wordPdfExported = $false }
$word = $null
$document = $null
try {
  $word = New-Object -ComObject Word.Application
  $record.wordAvailable = $true
  $word.Visible = $false
  $document = $word.Documents.Open((Join-Path $PWD 'docs/操作说明书-待实机核验.docx'))
  $document.Repaginate()
  $record.wordPageCount = $document.ComputeStatistics(2)
  $document.ExportAsFixedFormat((Join-Path $PWD 'docs/操作说明书-Word导出.pdf'), 17)
  $record.wordPdfExported = $true
} catch { $record.reason = $_.Exception.Message }
finally {
  if ($null -ne $document) { $document.Close($false) }
  if ($null -ne $word) { $word.Quit() }
}
$record | ConvertTo-Json | Set-Content docs/word-runtime-check.json -Encoding utf8
