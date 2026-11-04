#!/usr/bin/env python3
"""Check source provenance, original screenshot bytes and LibreOffice preview layout.
This intentionally does not infer Microsoft Word pagination.
"""
import hashlib
import importlib.metadata
import json
import re
import shutil
import subprocess
import zipfile
from pathlib import Path
import fitz
from docx import Document
from source_inventory import ROOT

hash_file = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
read_json = lambda path: json.loads(path.read_text(encoding='utf-8-sig'))
context = read_json(ROOT / 'docs/windows-ui/run-context.json')
desktop = read_json(ROOT / 'docs/windows-ui/desktop-evidence.json')
published = read_json(ROOT / 'docs/windows-ui/published-executable.json')
materials = read_json(ROOT / 'docs/material-validation.json')
inventory = read_json(ROOT / 'docs/source-inventory.json')
assert context['verify'] == context['publish'] == 'success'
assert desktop['status'] == published['status'] == 'passed'
assert not desktop['bindingErrors']
assert published['exit_code'] == 0
assert materials['screenshots_in_manual'] == 16
assert inventory['total'] >= 3500
for item in inventory['files']:
    path = ROOT / item['path']
    assert hash_file(path) == item['sha256']
    tested = subprocess.check_output(['git', 'show', f"{context['source_sha']}:avas-coverage-design/{item['path']}"], cwd=ROOT)
    assert tested == path.read_bytes(), item['path']
manual = ROOT / 'docs/materials/操作说明书-草稿.docx'
with zipfile.ZipFile(manual) as archive:
    assert archive.testzip() is None
    media = [hashlib.sha256(archive.read(name)).hexdigest() for name in archive.namelist() if name.startswith('word/media/')]
assert len(media) == 16
assert sorted(media) == sorted(x['sha256'] for x in materials['figures'])
for figure in materials['figures']:
    assert hash_file(ROOT / 'docs/windows-ui' / figure['file']) == figure['sha256']
manual_text = '\n'.join(p.text for p in Document(manual).paragraphs)
for i in range(1, 17):
    assert f'图{i}核对' in manual_text and f'图{i}  ' in manual_text
assert '一、软件介绍' in manual_text and '应用背景' in manual_text and '软件概述' in manual_text
information = Document(ROOT / 'docs/materials/软著信息采集表.docx')
functions = next(row.cells[1].text for row in information.tables[0].rows if row.cells[0].text == '软件主要功能')
han = len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff\U00020000-\U0002fa1f]', functions))
assert han > 500 and han == materials['major_function_unicode_han']
previews = []
for source_name, target_name in [('操作说明书-草稿', '操作说明书-LibreOffice预览'), ('软著信息采集表', '软著信息采集表-LibreOffice预览')]:
    source = ROOT / 'artifacts/material-layout' / (source_name + '.pdf')
    target = ROOT / 'docs/materials' / (target_name + '.pdf')
    shutil.copy2(source, target)
    pdf = fitz.open(target)
    pages = []
    for index, page in enumerate(pdf):
        content = [b for b in page.get_text('blocks') if b[6] == 0 and b[1] > 45 and b[3] < page.rect.height - 45]
        characters = sum(len(b[4]) for b in content)
        images = page.get_image_info()
        assert characters > 20 or images, f'Blank preview page {index + 1}'
        for image in images:
            assert (page.rect + (-1, -1, 1, 1)).contains(fitz.Rect(image['bbox'])), f'Image outside page {index + 1}'
        for block in content:
            assert block[0] >= 40 and block[2] <= page.rect.width - 40, f'Text outside body {index + 1}'
        pages.append({'page': index + 1, 'body_characters': characters, 'visible_images': len(images), 'blank': False})
    previews.append({'file': target.relative_to(ROOT).as_posix(), 'sha256': hash_file(target), 'rendered_pages': len(pdf), 'pages': pages})
result = {
    'renderer': 'LibreOfficeDev 26.8.0.0.alpha0, Linux, font substitution possible',
    'scope': 'Preview PDF body/image bounds, no blank pages, DOCX original image bytes, captions and source provenance; not Microsoft Word pagination',
    'source_matches_windows_verified_commit': True,
    'verified_source_commit': context['source_sha'],
    'windows_run': context['run_url'],
    'native_window_checks': len(desktop['checks']),
    'original_images_in_manual': len(media),
    'major_function_unicode_han': han,
    'word_actual_pages': None,
    'word_pagination_verified': False,
    'previews': previews
}
(ROOT / 'docs/material-layout.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({key: value for key, value in result.items() if key != 'previews'}, ensure_ascii=False, indent=2))
print([(item['file'], item['rendered_pages']) for item in previews])
