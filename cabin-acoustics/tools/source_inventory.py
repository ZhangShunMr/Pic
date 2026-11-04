from pathlib import Path
import hashlib,json
from docx import Document
from docx.shared import Pt,Cm
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
root=Path(__file__).resolve().parents[1]
files=sorted(p for p in (root/'src').rglob('*') if p.suffix in {'.cs','.razor','.css','.js'} and not {'bin','obj'}&set(p.parts))
rows=[]
for p in files:
 content=p.read_bytes();lines=content.decode().splitlines()
 rows.append({'path':str(p.relative_to(root)),'nonempty':sum(bool(x.strip()) for x in lines),'total':len(lines),'sha256':hashlib.sha256(content).hexdigest()})
data={'scope':'src/**/*.cs,razor,css,js 手写生产源码非空行；排除bin,obj,测试,依赖,文档,脚本生成产物','baseline':0,'minimum':3500,'nonempty':sum(x['nonempty'] for x in rows),'files':rows}
(root/'docs/source-inventory.json').write_text(json.dumps(data,ensure_ascii=False,indent=2))
text=[]
for row,p in zip(rows,files):
 text.extend(['='*70,row['path'],f"非空行 {row['nonempty']} / SHA256 {row['sha256']}",'='*70,p.read_text(),''])
(root/'docs/完整生产源码.txt').write_text('\n'.join(text).rstrip()+'\n')
doc=Document();section=doc.sections[0]
section.page_width=Cm(21);section.page_height=Cm(29.7);section.left_margin=section.right_margin=Cm(1.6)
normal=doc.styles['Normal'];normal.font.name='Consolas';normal.font.size=Pt(8);normal.paragraph_format.space_after=Pt(0)
normal.paragraph_format.line_spacing=1
section.header.paragraphs[0].text='车内低频声学模态与座位声压分布设计软件 V1.0 · 完整生产源码'
foot=section.footer.paragraphs[0];foot.alignment=2
field=OxmlElement('w:fldSimple');field.set(qn('w:instr'),'PAGE');foot._p.append(field)
for p in files:
 doc.add_heading(str(p.relative_to(root)),level=1)
 for i,line in enumerate(p.read_text().splitlines(),1):
  doc.add_paragraph(f'{i:04d}  {line}')
doc.save(root/'docs/完整生产源码排印稿.docx')
print(json.dumps({'nonempty':data['nonempty'],'files':len(files)},ensure_ascii=False))
