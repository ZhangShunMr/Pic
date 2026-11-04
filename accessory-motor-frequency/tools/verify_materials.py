from pathlib import Path
from datetime import datetime,timezone
import json,re,hashlib
import fitz
from PIL import Image
from docx import Document
ROOT=Path(__file__).resolve().parents[1]
DOCS=ROOT/'docs'
checks=[]
def check(name,condition,detail=''):
 assert condition,name+': '+detail
 checks.append({'name':name,'passed':True,'detail':detail})
manual=Document(DOCS/'操作说明书.docx')
headings=[p.text for p in manual.paragraphs if p.style.name=='Heading 1']
check('首部分为软件介绍',headings[0]=='1. 软件介绍')
check('按用户流程编写有效章节',len(headings)==15,str(headings))
text='\n'.join(p.text for p in manual.paragraphs)
for term in ['应用背景','软件概述','1800','1650','1950','保存','重开','导出','模拟','Word']:
 check('手册包含 '+term,term in text)
images=list(manual.inline_shapes)
check('14张不同真实截图进入手册',len(images)==14,str(len(images)))
image_hashes=[]
for rel in manual.part.rels.values():
 if 'image' in rel.reltype:image_hashes.append(hashlib.sha256(rel.target_part.blob).hexdigest())
check('手册没有重复截图',len(image_hashes)==len(set(image_hashes)))
pdf=fitz.open(DOCS/'pdf-preview/操作说明书.pdf')
check('LibreOffice实际预览至少10页',len(pdf)>=10,str(len(pdf)))
for index,page in enumerate(pdf):
 page_text=page.get_text()
 check('预览第'+str(index+1)+'页有效内容',len(page_text)>80,str(len(page_text)))
 check('预览第'+str(index+1)+'页页眉页脚','V1.0' in page_text and 'Word' in page_text)
 for block in page.get_text('blocks'):
  x0,y0,x1,y1=block[:4]
  check('预览第'+str(index+1)+'页文字在页内',x0>=-1 and y0>=-1 and x1<=page.rect.width+1 and y1<=page.rect.height+1)
info=Document(DOCS/'软著信息采集表.docx')
function=next(row.cells[1].text for row in info.tables[0].rows if row.cells[0].text=='软件主要功能')
han=len(re.findall('[\u3400-\u4dbf\u4e00-\u9fff]',function))
check('主要功能Unicode汉字超过500',han>500,str(han))
check('身份及日期保留待核实','待申请人核实/填写' in '\n'.join(c.text for row in info.tables[0].rows for c in row.cells))
info_pdf=fitz.open(DOCS/'pdf-preview/软著信息采集表.pdf')
info_text=re.sub(r'\s+','', ''.join(p.get_text() for p in info_pdf))
check('表格预览含功能末尾及字段','共振或故障根因' in info_text and '材料状态' in info_text)
inv=json.loads((DOCS/'source-inventory.json').read_text())
check('生产非空行达开工目标',inv['actual']>=3600,str(inv['actual']))
for item in inv['files']:
 check('源码哈希 '+item['file'],hashlib.sha256((ROOT/item['file']).read_bytes()).hexdigest()==item['sha256'])
for path in sorted((DOCS/'screenshots').glob('*.png')):
 img=Image.open(path)
 check('截图尺寸 '+path.name,img.width>=390 and img.height>=800,str(img.size))
 check('截图包含实际图形 '+path.name,len(img.convert('RGB').resize((32,32)).getcolors(1024) or [])>5)
meta=json.loads((DOCS/'material-evidence.json').read_text())
check('Word未执行明确标记',meta['wordPaginationVerified'] is False)
(DOCS/'materials-validation.json').write_text(json.dumps({'at':datetime.now(timezone.utc).isoformat(),'passed':len(checks),'checks':checks,'remaining':['Microsoft Word实际分页和申请人模板核对未执行']},ensure_ascii=False,indent=2))
print(json.dumps({'passed':len(checks),'manualPreviewPages':len(pdf),'han':han,'wordVerified':False},ensure_ascii=False))
