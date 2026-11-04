from pathlib import Path
import datetime
import hashlib
import json
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / 'docs'
OUT = ROOT / 'dist'
OUT.mkdir(exist_ok=True)
core = json.loads((DOCS / 'core-verification.json').read_text())
workflow = json.loads((DOCS / 'desktop-workflow-verification.json').read_text())
assert core['failures'] == workflow['failures'] == 0
assert 'Build succeeded.' in (DOCS / 'build-log.txt').read_text()
assert 'artifacts/win-x64/' in (DOCS / 'publish-log.txt').read_text()
manifest = json.loads((DOCS / 'source-inventory.json').read_text())
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
for row in manifest['files']:
    assert sha(ROOT / row['path']) == row['sha256']
exe = ROOT / 'artifacts/win-x64/TrimContact.Desktop.exe'
package = OUT / 'TrimContact-V2.0-win-x64.zip'
with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    z.write(exe, 'TrimContact-V2.0/TrimContact.Desktop.exe')
    z.write(ROOT / 'README.md', 'TrimContact-V2.0/README.md')
    for path in sorted((ROOT / 'samples').iterdir()):
        z.write(path, 'TrimContact-V2.0/samples/' + path.name)
    for name in ['操作说明书-待Windows核验.docx', '软著信息采集表-草稿.docx', 'Windows实机验收.md', '验证记录.md']:
        z.write(DOCS / name, 'TrimContact-V2.0/docs/' + name)
    assert z.testzip() is None
assert package.stat().st_size < 100 * 1024**2
info = {
    'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'rid': 'win-x64', 'selfContained': True, 'singleFile': True, 'compression': True,
    'exeSha256': sha(exe), 'exeBytes': exe.stat().st_size,
    'zipSha256': sha(package), 'zipBytes': package.stat().st_size,
    'nativeFile': subprocess.check_output(['file', str(exe)], text=True).strip(),
    'crossCompiledOn': 'Linux x64', 'windowsRuntimeVerified': False,
    'realWpfScreenshots': 0, 'zipCrcVerified': True,
    'coreChecks': core['count'], 'workflowChecks': workflow['count']
}
(DOCS / 'release-evidence.json').write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding='utf-8')
(OUT / 'SHA256SUMS.txt').write_text(info['zipSha256'] + '  ' + package.name + '\n', encoding='utf-8')
print(json.dumps(info, ensure_ascii=False, indent=2))
