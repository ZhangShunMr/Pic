from pathlib import Path
import fitz,json,sys
from PIL import Image,ImageDraw
from docx import Document
ROOT=Path(__file__).resolve().parents[1]
PREVIEW=Path(sys.argv[1]) if len(sys.argv)>1 else Path('/workspace/trim-doc-preview')
report={}
for name in ['操作说明书','软著信息采集表']:
 pdf=PREVIEW/(name+'.pdf');d=fitz.open(pdf);rows=[];thumbs=[]
 for i,page in enumerate(d):
  blocks=page.get_text('blocks')
  bad=[list(b[:4]) for b in blocks if b[0]<0 or b[1]<0 or b[2]>page.rect.width+1 or b[3]>page.rect.height+1]
  text=page.get_text()
  rows.append({'page':i+1,'characters':len(text),'images':len(page.get_images()),'out_of_page_blocks':bad,'first_lines':text.splitlines()[:4]})
  assert not bad,(name,i,bad)
  assert len(text)>180,(name,i,'orphan or insufficient text')
  pix=page.get_pixmap(matrix=fitz.Matrix(.6,.6));im=Image.frombytes('RGB',[pix.width,pix.height],pix.samples)
  im.thumbnail((300,425));canvas=Image.new('RGB',(310,455),'#e0e6e2');canvas.paste(im,((310-im.width)//2,10));ImageDraw.Draw(canvas).text((10,438),str(i+1),fill='black');thumbs.append(canvas)
  if name=='操作说明书':page.get_pixmap(matrix=fitz.Matrix(1.2,1.2)).save(PREVIEW/f'manual-page-{i+1:02}.png')
 sheet=Image.new('RGB',(5*310,((len(thumbs)+4)//5)*455),'white')
 for i,im in enumerate(thumbs):sheet.paste(im,((i%5)*310,(i//5)*455))
 sheet.save(PREVIEW/(name+'-contact-sheet.png'))
 report[name]={'pages':len(d),'renderer':'LibreOffice, NOT Microsoft Word','rows':rows}
 if name=='操作说明书':assert len(d)>=10
manifest=json.loads((ROOT/'docs/materials-check.json').read_text())
information=Document(ROOT/'docs/软著信息采集表.docx')
function_text=next(r.cells[1].text for r in information.tables[0].rows if r.cells[0].text=='主要功能')
han_count=sum('\u3400'<=c<='\u4dbf' or '\u4e00'<=c<='\u9fff' or '\U00020000'<=c<='\U0002fa1f' for c in function_text)
assert han_count==manifest['major_function_han_count'] and han_count>500
assert manifest['production_nonblank']>=3500
manual=Document(ROOT/'docs/操作说明书.docx')
assert manual.paragraphs[0].text=='1 软件介绍'
assert len(manual.inline_shapes)==manifest['unique_figures']
report['formal_status']='Word pagination NOT VERIFIED; applicant template and ownership facts pending'
(ROOT/'docs/document-layout-check.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print({k:v['pages'] for k,v in report.items() if isinstance(v,dict)})
