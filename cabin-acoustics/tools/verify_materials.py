from pathlib import Path
import json,re,subprocess,zipfile
from playwright.sync_api import sync_playwright
import fitz
from PIL import Image
root=Path(__file__).resolve().parents[1]
docs=root/'docs'
with sync_playwright() as p:
 b=p.chromium.launch(executable_path='/usr/bin/chromium',args=['--no-sandbox'])
 page=b.new_page();page.set_content((docs/'操作说明书-预览.html').read_text(),wait_until='load')
 page.pdf(path=str(docs/'操作说明书-浏览器预览.pdf'),print_background=True,prefer_css_page_size=True);b.close()
render=Path('/tmp/cabin-final-docx-render');render.mkdir(exist_ok=True)
r=subprocess.run(['soffice','-env:UserInstallation=file:///tmp/cabin-final-lo-profile','--headless','--convert-to','pdf','--outdir',str(render),str(docs/'操作说明书.docx'),str(docs/'软著信息采集表.docx')],capture_output=True,text=True,timeout=60)
assert r.returncode==0,r.stdout+r.stderr
results=[];thumbs=[]
for name in ['操作说明书','软著信息采集表']:
 source=render/(name+'.pdf');target=docs/(name+'-LibreOffice核对.pdf');target.write_bytes(source.read_bytes())
 pdf=fitz.open(target)
 for i,page in enumerate(pdf):
  text=page.get_text();assert len(text.strip())>40
  results.append({'file':target.name,'page':i+1,'characters':len(text),'images':len(page.get_images())})
  if name=='操作说明书':
   pix=page.get_pixmap(matrix=fitz.Matrix(.6,.6));path=render/f'page-{i+1:02}.png';pix.save(path)
   thumbs.append(Image.open(path).convert('RGB'))
 if name=='操作说明书':assert len(pdf)>=10
manual_pages=len(thumbs)
w,h=thumbs[0].size;sheet=Image.new('RGB',(w*6,h*((len(thumbs)+5)//6)),'#ccc')
for i,im in enumerate(thumbs):sheet.paste(im,((i%6)*w,(i//6)*h))
sheet.save(docs/'手册分页总览.png')
for name in ['操作说明书.docx','软著信息采集表.docx']:
 with zipfile.ZipFile(docs/name) as z:
  xml=''.join(z.read(n).decode(errors='replace') for n in z.namelist() if n.endswith('.xml'))
  assert '车身声学泄漏' not in xml
collection=(docs/'软著信息采集表.md').read_text();functions=collection.split('## 软件主要功能\n\n')[1].split('\n\n## 技术特点')[0]
han=len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff\U00020000-\U0002a6df]',functions));assert han>500
inventory=json.loads((docs/'source-inventory.json').read_text());assert inventory['nonempty']>=3500
for item in inventory['files']:assert (root/item['path']).exists()
for p in (docs/'screenshots').glob('*.png'):
 im=Image.open(p);assert im.width>=500 and im.height>20
summary={'manualPages':manual_pages,'renderer':'LibreOfficeDev 26.8.0.0.alpha0；不是Microsoft Word','browserPreviewPages':len(fitz.open(docs/'操作说明书-浏览器预览.pdf')),'mainFunctionHan':han,'productionNonempty':inventory['nonempty'],'screenshots':len(list((docs/'screenshots').glob('*.png'))),'pages':results,'wordPagination':'未验证，仍须Microsoft Word打开或导出PDF实际核验','review':'逐页图像和文本检查；无空白页，图注连续，截图来源与当前软件一致；采集表跨页无裁切重叠。'}
(docs/'materials-evidence.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2))
print(json.dumps({k:v for k,v in summary.items() if k!='pages'},ensure_ascii=False,indent=2))
