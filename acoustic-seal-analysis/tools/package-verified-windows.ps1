$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$evidence = Get-Content docs/screenshots/published-exe-evidence.json -Raw | ConvertFrom-Json
$exe = Join-Path $PWD 'artifacts/win-x64/AcousticSeal.Desktop.exe'
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
if ($hash -ne $evidence.sha256 -or $evidence.normalExitCode -ne 0) { throw 'Published exe does not match successful native process evidence' }
$archive = 'releases/AcousticSeal-V3.0-win-x64.zip'
Compress-Archive -Path artifacts/win-x64/* -DestinationPath $archive -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $PWD $archive))
try {
  $entry = $zip.Entries | Where-Object { $_.FullName -eq 'AcousticSeal.Desktop.exe' }
  if ($null -eq $entry) { throw 'Archive does not contain native exe' }
  $stream = $entry.Open()
  try {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $archivedExeHash = [Convert]::ToHexString($algorithm.ComputeHash($stream))
    if ($archivedExeHash -ne $hash) { throw 'Archived executable differs from tested binary' }
  } finally { $stream.Dispose() }
} finally { $zip.Dispose() }
@{
  file = $archive
  sha256 = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
  bytes = (Get-Item $archive).Length
  rid = 'win-x64'
  self_contained = $true
  built_on = [Environment]::OSVersion.ToString()
  sourceCommit = $env:GITHUB_SHA
  workflowRun = "https://github.com/$env:GITHUB_REPOSITORY/actions/runs/$env:GITHUB_RUN_ID"
  windows_runtime_executed = $true
  native_exit_code = 0
  tested_exe_sha256 = $hash.ToLowerInvariant()
  archived_exe_hash_verified = $true
} | ConvertTo-Json | Set-Content docs/release-manifest.json -Encoding utf8
