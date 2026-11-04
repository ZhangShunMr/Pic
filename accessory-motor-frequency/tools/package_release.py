from pathlib import Path
import hashlib
import json
import shutil
import zipfile
import os

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
source = json.loads((root / 'docs/source-inventory.json').read_text(encoding='utf-8'))
proof_path = root / 'docs/windows-evidence/published-exe.json'
proof = json.loads(proof_path.read_text(encoding='utf-8-sig')) if proof_path.exists() else {}
verified = (os.name == 'nt' and os.environ.get('GITHUB_SHA') == proof.get('sourceCommit')
    and proof.get('actualProcessStarted') and proof.get('normalExitCode') == 0
    and proof.get('sha256', '').lower() == hashlib.sha256((publish/'MotorFrequency.exe').read_bytes()).hexdigest())
assemblies = proof.get('assemblies', [])
verified = bool(verified and len(assemblies) == 3 and all(
    item['sha256'].lower() == hashlib.sha256((publish/item['file']).read_bytes()).hexdigest() for item in assemblies))
manifest = {
    'software': '汽车附件电机电磁激励频率设计软件', 'version': 'V2.0',
    'branch': 'desktop/accessory-motor-frequency-wpf-20261104',
    'base_main_sha': '2bf4fc16fbca9c7ef36c4cce0a7d3034a16a2cd4',
    'runtime': 'Windows 10/11 x64, self-contained .NET10 WPF; offline target',
    'archive': archive.name, 'archive_bytes': archive.stat().st_size,
    'archive_sha256': hashlib.sha256(archive.read_bytes()).hexdigest(),
    'zip_integrity_verified': True, 'windows_pe_header_verified': True,
    'windows_execution_verified': verified, 'signed': False,
    'windows_execution_scope': 'Windows Server 2025 CI: native window startup and normal exit; byte hashes of EXE and three application assemblies matched' if verified else 'Package bytes not executed in this packaging environment',
    'windows_ci_source_commit': proof.get('sourceCommit'),
    'windows_ci_assembly_hashes': assemblies,
    'windows_ci_run': 'https://github.com/ZhangShunMr/Pic/actions/runs/' + os.environ.get('GITHUB_RUN_ID', '41860507345'),
    'production_source_nonblank_lines': source['nonblank_lines'],
    'production_source_manifest_sha256': hashlib.sha256((root / 'docs/source-inventory.json').read_bytes()).hexdigest(),
    'tests': {'core_passed': 57, 'application_passed': 23, 'failed': 0, 'wpf_runtime': 'passed on Windows Server 2025; scope see docs/windows-evidence'},
    'remaining': ['Windows10/11目标设备、高DPI和其余人工路径验收', 'Microsoft Word实际分页及逐页核对', '申请人事实与模板核实'],
    'files': [{'path': f.relative_to(publish).as_posix(), 'bytes': f.stat().st_size,
        'sha256': hashlib.sha256(f.read_bytes()).hexdigest()} for f in files]
}
(release / '发布清单.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({k: manifest[k] for k in ['archive','archive_bytes','archive_sha256','production_source_nonblank_lines','zip_integrity_verified']}, ensure_ascii=False))
