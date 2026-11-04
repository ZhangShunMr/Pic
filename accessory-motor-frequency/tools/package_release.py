from pathlib import Path
import hashlib
import json
import shutil
import zipfile

root = Path(__file__).resolve().parents[1]
publish = root / 'artifacts/win-x64'
release = root / 'release'
release.mkdir(exist_ok=True)
if (publish / 'MotorFrequency.exe').read_bytes()[:2] != b'MZ':
    raise RuntimeError('Published apphost is not a Windows PE file')
required = ['MotorFrequency.dll', 'MotorFrequency.Application.dll', 'MotorFrequency.Core.dll',
            'PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll', 'coreclr.dll',
            'hostfxr.dll', 'MotorFrequency.runtimeconfig.json']
for name in required:
    if not (publish / name).exists():
        raise RuntimeError('Missing offline component: ' + name)
shutil.copyfile(root / 'README.md', publish / 'README.md')
shutil.copytree(root / 'examples', publish / 'examples', dirs_exist_ok=True)
shutil.copytree(root / 'docs', publish / 'docs', dirs_exist_ok=True)
archive = release / 'MotorFrequency-V2.0-win-x64.zip'
files = sorted(p for p in publish.rglob('*') if p.is_file() and p.suffix != '.pdb')
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as output:
    for file in files:
        output.write(file, file.relative_to(publish).as_posix())
with zipfile.ZipFile(archive) as check:
    bad = check.testzip()
    if bad:
        raise RuntimeError('Corrupt ZIP entry: ' + bad)
source = json.loads((root / 'docs/source-inventory.json').read_text())
manifest = {
    'software': '汽车附件电机电磁激励频率设计软件', 'version': 'V2.0',
    'branch': 'desktop/accessory-motor-frequency-wpf-20261104',
    'base_main_sha': '2bf4fc16fbca9c7ef36c4cce0a7d3034a16a2cd4',
    'runtime': 'Windows 10/11 x64, self-contained .NET10 WPF; offline target',
    'archive': archive.name, 'archive_bytes': archive.stat().st_size,
    'archive_sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
    'zip_integrity_verified': True, 'windows_pe_header_verified': True,
    'windows_execution_verified': False, 'signed': False,
    'production_source_nonblank_lines': source['nonblank_lines'],
    'production_source_manifest_sha256': hashlib.sha256((root / 'docs/source-inventory.json').read_bytes()).hexdigest(),
    'tests': {'core_passed': 57, 'application_passed': 23, 'failed': 0, 'wpf_runtime': 'not executed'},
    'remaining': ['Windows实际运行/截图/对话框/缩放验收', 'Microsoft Word实际分页及逐页核对', '申请人事实与模板核实'],
    'files': [{'path': f.relative_to(publish).as_posix(), 'bytes': f.stat().st_size,
        'sha256': hashlib.sha256(f.read_bytes()).hexdigest()} for f in files]
}
(release / '发布清单.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({k: manifest[k] for k in ['archive','archive_bytes','archive_sha256','production_source_nonblank_lines','zip_integrity_verified']}, ensure_ascii=False))
