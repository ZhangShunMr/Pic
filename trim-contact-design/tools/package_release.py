from pathlib import Path
import datetime
import hashlib
import json
import struct
import platform
import zipfile

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / 'docs'
OUT = ROOT / 'dist'
OUT.mkdir(exist_ok=True)
def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))
windows = DOCS / 'windows'
core = read_json(windows / 'core-verification.json') if (windows / 'core-verification.json').exists() else read_json(DOCS / 'core-verification.json')
workflow = read_json(windows / 'desktop-workflow-verification.json') if (windows / 'desktop-workflow-verification.json').exists() else read_json(DOCS / 'desktop-workflow-verification.json')
published = read_json(windows / 'published-exe-evidence.json') if (windows / 'published-exe-evidence.json').exists() else {}
ui = read_json(windows / 'windows-ui-evidence.json') if (windows / 'windows-ui-evidence.json').exists() else {}
assert core['failures'] == workflow['failures'] == 0
build_log = windows / 'build-log.txt' if (windows / 'build-log.txt').exists() else DOCS / 'build-log.txt'
assert 'Build succeeded.' in build_log.read_text(encoding='utf-8-sig')
assert 'artifacts/win-x64/' in (DOCS / 'publish-log.txt').read_text()
manifest = read_json(DOCS / 'source-inventory.json')
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
for row in manifest['files']:
    assert sha(ROOT / row['path']) == row['sha256']
exe = ROOT / 'artifacts/win-x64/TrimContact.Desktop.exe'
binary = exe.read_bytes()
assert binary[:2] == b'MZ'
pe_offset = struct.unpack_from('<I', binary, 60)[0]
assert binary[pe_offset:pe_offset+4] == b'PE\0\0'
assert struct.unpack_from('<H', binary, pe_offset+4)[0] == 0x8664
native_verified = bool(published) and published.get('normalExitCode') == 0 and published.get('sha256', '').lower() == sha(exe) and ui.get('failed') == 0 and ui.get('sourceCommit') == published.get('sourceCommit')
package = OUT / 'TrimContact-V2.0-win-x64.zip'
with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    z.write(exe, 'TrimContact-V2.0/TrimContact.Desktop.exe')
    z.write(ROOT / 'README.md', 'TrimContact-V2.0/README.md')
    for path in sorted((ROOT / 'samples').iterdir()):
        z.write(path, 'TrimContact-V2.0/samples/' + path.name)
    for name in ['操作说明书-待Windows核验.docx', '软著信息采集表-草稿.docx', 'Windows实机验收.md', '验证记录.md']:
        z.write(DOCS / name, 'TrimContact-V2.0/docs/' + name)
    for path in sorted((windows / 'screenshots').glob('*.png')):
        z.write(path, 'TrimContact-V2.0/docs/windows/screenshots/' + path.name)
    for name in ['windows-ui-evidence.json', 'published-exe-evidence.json', 'word-pagination.json']:
        path = windows / name
        if path.exists(): z.write(path, 'TrimContact-V2.0/docs/windows/' + name)
    for name in ['source-inventory.json', 'materials-check.json', 'screenshot-inventory.json']:
        z.write(DOCS / name, 'TrimContact-V2.0/docs/' + name)
    assert z.testzip() is None
assert package.stat().st_size < 100 * 1024**2
info = {
    'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
    'rid': 'win-x64', 'selfContained': True, 'singleFile': True, 'compression': True,
    'exeSha256': sha(exe), 'exeBytes': exe.stat().st_size,
    'zipSha256': sha(package), 'zipBytes': package.stat().st_size,
    'nativeFile': 'PE32+ Microsoft Windows GUI x86-64; header checked',
    'builtOn': platform.platform(), 'windowsRuntimeVerified': native_verified,
    'publishedRunUrl': published.get('runUrl') if native_verified else None,
    'realWpfScreenshots': len(list((windows / 'screenshots').glob('*.png'))), 'zipCrcVerified': True,
    'coreChecks': core['count'], 'workflowChecks': workflow['count']
}
(DOCS / 'release-evidence.json').write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding='utf-8')
(OUT / 'SHA256SUMS.txt').write_text(info['zipSha256'] + '  ' + package.name + '\n', encoding='utf-8')
print(json.dumps(info, ensure_ascii=False, indent=2))
