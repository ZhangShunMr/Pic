"""Verify source/material consistency, without claiming Word pagination."""
from pathlib import Path
import hashlib
import json
import re
import zipfile
from docx import Document

root = Path(__file__).resolve().parents[1]
docs = root / 'docs'
source = json.loads((docs / 'source-inventory.json').read_text(encoding='utf-8'))
materials = json.loads((docs / 'materials-evidence.json').read_text(encoding='utf-8'))
checks = []

def check(name, condition):
    checks.append(dict(name=name, status='passed' if condition else 'failed'))
    if not condition:
        raise AssertionError(name)

check('minimum production nonblank', source['productionNonblank'] >= 3500)
for entry in source['files']:
    path = root / entry['path']
    check('production source hash ' + entry['path'], hashlib.sha256(path.read_bytes()).hexdigest() == entry['sha256'])
check('major function Unicode Hanzi exceeds 500', materials['majorFunctionHanzi'] > 500)
check('unverified Word count remains explicit', materials['manualWordPageCount'] is None)
manual = Document(docs / '操作说明书-草稿.docx')
first_part = next(p.text for p in manual.paragraphs if p.style.name == 'Heading 1')
check('manual starts with software introduction', first_part == '第一部分 软件介绍')
check('manual contains native screenshot figures', len(manual.inline_shapes) >= 10)
check('native image count matches material manifest', len(manual.inline_shapes) == materials['nativeWpfFiguresEmbedded'])
ui = json.loads((docs / 'screenshots/windows-ui-evidence.json').read_text(encoding='utf-8-sig'))
exe = json.loads((docs / 'screenshots/published-exe-evidence.json').read_text(encoding='utf-8-sig'))
check('actual Windows WPF checks passed', ui['failed'] == 0 and all(c['status'] == 'passed' for c in ui['checks']))
check('actual published executable started and exited normally', exe['actualProcessStarted'] and exe['normalExitCode'] == 0)
check('native images and published exe share source commit', ui['sourceCommit'] == exe['sourceCommit'])
review = json.loads((docs / 'screenshot-review.json').read_text(encoding='utf-8'))
check('native screenshot review matches source commit', review['sourceCommit'] == ui['sourceCommit'])
for item in review['images']:
    check('reviewed image hash ' + item['path'], hashlib.sha256((docs / 'screenshots' / item['path']).read_bytes()).hexdigest() == item['sha256'])
check('native binding error log empty', not (docs / 'screenshots/binding-errors.log').read_text(encoding='utf-8-sig').strip())
for name in ['test-evidence.json', 'test-windows.json']:
    evidence = json.loads((docs / name).read_text(encoding='utf-8-sig'))
    check(name + ' all core tests passed', evidence['failed'] == 0 and evidence['total'] == 70)
check('Release build success recorded', 'Build succeeded.' in (docs / 'build-linux.log').read_text(encoding='utf-8'))
check('actual solution file exists', (root / 'CabinAcoustics.sln').is_file())
for path in (root / 'src').rglob('*'):
    if not path.is_file() or {'bin', 'obj'}.intersection(path.parts):
        continue
    check('no browser source ' + str(path.relative_to(root)), path.suffix not in {'.razor', '.js', '.html', '.css'})
    if path.suffix == '.csproj':
        check('no web SDK ' + path.name, 'Microsoft.NET.Sdk.Web' not in path.read_text())
report = dict(checks=checks, passed=len(checks), failed=0,
              limits=['Microsoft Word actual pagination pending', 'Windows 10/11 client hardware and DPI manual verification pending'])
(docs / 'delivery-evidence.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(f'{len(checks)} source, material and evidence consistency checks passed. Word pagination remains pending.')
