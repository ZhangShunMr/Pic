"""Inspect actual rendered pages; never treat manual page breaks as Word evidence."""
from pathlib import Path
import sys,json,re,hashlib
from pypdf import PdfReader
from docx import Document
sys.path.insert(0,str(Path(__file__).parent))
from manual_content import PAGES,NAME
root=Path(__file__).resolve().parents[1];docs=root/'docs'
manual=PdfReader(docs/'操作说明书-LibreOffice预览.pdf');info=PdfReader(docs/'软著信息采集表-LibreOffice预览.pdf')
assert len(manual.pages)>=10
assert len(manual.pages)==len(PAGES),(len(manual.pages),len(PAGES))
pages=[]
for index,page in enumerate(manual.pages):
 text=page.extract_text();compact=re.sub(r'\s','',text)
 assert re.sub(r'\s','',PAGES[index][0]) in compact
 assert len(text)>250
 assert not re.search(r'@[A-Za-z]+\.',text)
 assert 'V1.0' in compact
 pages.append({'page':index+1,'section':PAGES[index][0],'textCharacters':len(text),'images':len(page.images),'contentChecked':True})
text=''.join(p.extract_text() for p in info.pages)
assert '1269' in text
assert '待申请人' in text
build=json.loads((docs/'material-build.json').read_text());inventory=json.loads((docs/'source-inventory.json').read_text())
assert build['functionsHanCount']>500
assert inventory['productionNonblank']>=3500
assert len({f['file'] for f in build['figures']})==len(build['figures'])
for f in inventory['files']:
 assert hashlib.sha256((root/f['path']).read_bytes()).hexdigest()==f['sha256']
manualdoc=Document(docs/'操作说明书.docx')
assert len(manualdoc.inline_shapes)==len(build['figures'])
result={'renderer':'LibreOffice Writer PDF export','manualPages':len(manual.pages),'informationPages':len(info.pages),'manualFigureCount':len(build['figures']),'functionsHanCount':build['functionsHanCount'],'productionNonblank':inventory['productionNonblank'],'sourceHashesVerified':True,'pages':pages,'visualReview':'已逐页检查渲染总览，并抽查原尺寸页面；无空白页、截图重复、表格或图注截断。','wordPagination':{'status':'pending','reason':'本环境无Microsoft Word；LibreOffice和浏览器页数不能代替规范要求的Word实际分页。'},'applicantFacts':'身份、权属、开发完成、发表信息待申请人核实','template':'没有申请人专用模板；按仓库现有A4材料版式制作可编辑稿'}
(docs/'materials-evidence.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
print(json.dumps({k:v for k,v in result.items() if k!='pages'},ensure_ascii=False,indent=2))
