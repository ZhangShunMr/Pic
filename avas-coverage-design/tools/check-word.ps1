$ErrorActionPreference = 'Stop'
$evidence = @{at=(Get-Date).ToUniversalTime().ToString('o');word_available=$false;actual_word_pages=$null;minimum_pages=10;verified=$false;status='not-executed'}
$word=$null; $document=$null
try {
    $word=New-Object -ComObject Word.Application
    $evidence.word_available=$true
    $word.Visible=$false
    $word.DisplayAlerts=0
    $path=(Resolve-Path 'docs/materials/操作说明书-草稿.docx').Path
    $document=$word.Documents.Open($path,$false,$true)
    $document.Repaginate()
    $pages=$document.ComputeStatistics(2)
    $evidence.actual_word_pages=$pages
    $evidence.verified=($pages -ge 10)
    $evidence.status=if ($pages -ge 10) {'word-pagination-passed-visual-review-required'} else {'word-pagination-below-minimum'}
    $document.ExportAsFixedFormat([IO.Path]::GetFullPath('docs/windows-ui/manual-word.pdf'),17)
} catch {
    $evidence.status='word-unavailable-or-execution-failed'
    $evidence.reason=$_.Exception.Message
} finally {
    if ($document) { $document.Close($false) }
    if ($word) { $word.Quit() }
    New-Item docs/windows-ui -ItemType Directory -Force | Out-Null
    $evidence | ConvertTo-Json | Set-Content docs/windows-ui/word-pagination.json -Encoding utf8
}
