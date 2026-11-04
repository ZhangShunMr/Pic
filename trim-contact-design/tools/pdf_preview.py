from pathlib import Path
import datetime
import hashlib
import json
import os
import subprocess
import fitz

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'docs/preview'
rows = []
for kind, name in [('manual', '操作说明书-待Windows核验'), ('collection', '软著信息采集表-草稿')]:
    pdf = OUT / (name + '.pdf')
    source = ROOT / 'docs' / (name + '.docx')
    document = fitz.open(pdf)
    checks = []
    for index, page in enumerate(document):
        image = OUT / f'{kind}-page-{index+1:02d}.png'
        page.get_pixmap(matrix=fitz.Matrix(1.3, 1.3), alpha=False).save(image)
        text = page.get_text().strip()
        embedded_images = len(page.get_images())
        checks.append({'page': index+1, 'textCharacters': len(text), 'embeddedImages': embedded_images,
                       'nonempty': len(text) > 60 or embedded_images > 0})
    assert all(row['nonempty'] for row in checks), checks
    if kind == 'manual':
        assert len(document) >= 10
    rows.append({'document': source.name, 'sourceSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                 'pdfSha256': hashlib.sha256(pdf.read_bytes()).hexdigest(),
                 'actualLibreOfficePdfPages': len(document), 'pageChecks': checks,
                 'humanPerPageInspection': False})
result = {'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'sourceCommit': os.environ.get('GITHUB_SHA'),
          'runUrl': 'https://github.com/' + os.environ.get('GITHUB_REPOSITORY', '') + '/actions/runs/' + os.environ.get('GITHUB_RUN_ID', ''),
          'renderer': subprocess.check_output(['libreoffice', '--version'], text=True).strip(),
          'wordPaginationVerified': False,
          'note': 'Actual LibreOffice DOCX-to-PDF pagination and per-page renders; does not establish Microsoft Word pagination. Human inspection is recorded separately.',
          'documents': rows}
(OUT / 'preview-evidence.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))
