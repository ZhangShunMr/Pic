"""Render preview using Chromium, detect clipping, and inspect every PDF page."""

import json
import functools
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import re
from pathlib import Path
from playwright.sync_api import sync_playwright
from pypdf import PdfReader

ROOT = Path(__file__).resolve().parents[1]
DOCS = ROOT / 'docs'
server = ThreadingHTTPServer(("127.0.0.1", 0), functools.partial(SimpleHTTPRequestHandler, directory=str(DOCS)))
threading.Thread(target=server.serve_forever, daemon=True).start()
with sync_playwright() as playwright:
    browser = playwright.chromium.launch(executable_path='/usr/bin/chromium', headless=True, args=['--no-sandbox'])
    page = browser.new_page()
    page.goto(f'http://127.0.0.1:{server.server_port}/操作说明书-预览.html')
    page.wait_for_function('Array.from(document.images).every(image => image.complete && image.naturalWidth > 0)')
    measurements = page.evaluate('''() => Array.from(document.querySelectorAll('.sheet')).map((sheet, index) => {
      const folio = sheet.querySelector('.folio').getBoundingClientRect();
      const content = Array.from(sheet.children).filter(element => !element.classList.contains('folio'));
      const bottom = Math.max(...content.map(element => element.getBoundingClientRect().bottom));
      return {page: index + 1, title: sheet.querySelector('h1').textContent, contentBottom: bottom, footerTop: folio.top, fits: bottom < folio.top - 5};
    })''')
    assert all(record['fits'] for record in measurements), measurements
    page.pdf(path=str(DOCS / '操作说明书-浏览器预览.pdf'), print_background=True, prefer_css_page_size=True)
    browser.close()
server.shutdown()
server.server_close()
reader = PdfReader(DOCS / '操作说明书-浏览器预览.pdf')
assert len(reader.pages) == 16, len(reader.pages)
page_evidence = []
for index, pdf_page in enumerate(reader.pages):
    text = pdf_page.extract_text()
    han = len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff]', text))
    assert han > 200, (index + 1, han)
    page_evidence.append({'page': index + 1, 'hanCharacters': han, 'textPresent': True, 'contentFits': measurements[index]['fits']})
result = {
    'previewEngine': 'headless Chromium print-to-PDF',
    'previewPdfPages': len(reader.pages),
    'pages': page_evidence,
    'figures': len(list((DOCS / 'screenshots').glob('*.png'))),
    'wordActualPagination': '未执行；此PDF并非Microsoft Word导出，正式验收仍需Word实际分页与版式复核',
    'applicantTemplate': '未提供；当前为A4通用可编辑稿',
}
(DOCS / 'materials-evidence.json').write_text(json.dumps(result, ensure_ascii=False, indent=2))
print(json.dumps(result, ensure_ascii=False))
