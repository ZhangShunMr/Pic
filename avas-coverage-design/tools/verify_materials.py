"""Verify deliverable identities, functional-field Han count and actual image links."""
import hashlib,json,re
from pathlib import Path
from docx import Document
ROOT=Path(__file__).resolve().parents[1];docs=ROOT/'docs'
manual=Document(docs/'操作说明书.docx');info=Document(docs/'软著信息采集表.docx')
headings=[p.text for p in manual.paragraphs if p.style.name=='Heading 1']
assert headings[0]=='软件介绍'
assert len(headings)>=10
fields=[(row.cells[0].text,row.cells[1].text) for table in info.tables for row in table.rows]
functions=next(v for k,v in fields if '软件主要功能' in k)
han=len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff\U00020000-\U0002fa1f]',functions));assert han>500
assert functions==(docs/'软件主要功能.txt').read_text()
pages=json.loads((ROOT/'tools/manual-content.json').read_text())
images=[x['image'] for x in pages if 'image' in x]
assert len(images)==len(set(images)),'手册主截图重复'
assert all((docs/'screenshots'/x).exists() for x in images)
assert len(manual.inline_shapes)==len(images)
assert (ROOT/'AvasCoverage.sln').exists()
inventory=json.loads((docs/'source-inventory.json').read_text());assert inventory['total']>=3500
for row in inventory['files']:
 assert row['purpose']
 assert hashlib.sha256((ROOT/row['file']).read_bytes()).hexdigest()==row['sha256']
core=json.loads((docs/'core-verification.json').read_text());assert core['failed']==0
browser=json.loads((docs/'browser-verification.json').read_text());assert all(x['status']=='passed' for x in browser['checks']) and not browser['page_errors'] and not browser['console_errors']
result={'manual_first_section':headings[0],'manual_sections':len(headings),'manual_unique_images':len(images),'major_function_han_count':han,'production_nonempty_lines':inventory['total'],'core_passed':core['passed'],'browser_passed':len(browser['checks']),'word_pagination_verified':False,'dedicated_template_provided':False,'requirements_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT.parent/'软件设计准则.md',ROOT.parent/'软著编写要求.md']}}
(docs/'材料结构检查.json').write_text(json.dumps(result,ensure_ascii=False,indent=2));print(json.dumps(result,ensure_ascii=False,indent=2))
