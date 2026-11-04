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
    for previous in OUT.glob(kind + '-page-*.png'):
        previous.unlink()
    pdf = OUT / (name + '.pdf')
    source = ROOT / 'docs' / (name + '.docx')
    committed_source = subprocess.check_output(['git', 'show', 'HEAD:trim-contact-design/docs/' + source.name], cwd=ROOT)
    assert source.read_bytes() == committed_source, 'Renderer changed the committed input DOCX: ' + source.name
    document = fitz.open(pdf)
    checks = []
    for index, page in enumerate(document):
        image = OUT / f'{kind}-page-{index+1:02d}.png'
        page.get_pixmap(matrix=fitz.Matrix(1.3, 1.3), alpha=False).save(image)
        text = page.get_text().strip()
        body_text = ''.join(block[4] for block in page.get_text('blocks') if block[1] > 55 and block[3] < page.rect.height - 50 and block[6] == 0).strip()
        embedded_images = len(page.get_image_info())
        checks.append({'page': index+1, 'textCharacters': len(text), 'bodyTextCharacters': len(body_text), 'visibleImages': embedded_images,
                       'nonempty': len(body_text) > 20 or embedded_images > 0})
    assert all(row['nonempty'] for row in checks), checks
    if kind == 'manual':
        assert len(document) >= 10
    rows.append({'document': source.name, 'sourceSha256': hashlib.sha256(source.read_bytes()).hexdigest(),
                 'pdfSha256': hashlib.sha256(pdf.read_bytes()).hexdigest(),
                 'actualLibreOfficePdfPages': len(document), 'pageChecks': checks,
                 'humanPerPageInspection': False})
result = {'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
          'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
          'ciTriggerCommit': os.environ.get('GITHUB_SHA'),
          'runUrl': 'https://github.com/' + os.environ.get('GITHUB_REPOSITORY', '') + '/actions/runs/' + os.environ.get('GITHUB_RUN_ID', ''),
          'renderer': subprocess.check_output(['libreoffice', '--version'], text=True).strip(),
          'wordPaginationVerified': False,
          'note': 'Actual LibreOffice DOCX-to-PDF pagination and per-page renders; does not establish Microsoft Word pagination. Human inspection is recorded separately.',
          'documents': rows}
(OUT / 'preview-evidence.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False, indent=2))
