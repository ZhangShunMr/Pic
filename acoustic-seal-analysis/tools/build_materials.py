from pathlib import Path
import re,json
from docx import Document
from docx.shared import Pt,Cm
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
r=Path(__file__).resolve().parents[1]
manual=(r/'docs/操作说明书-待实机核验.md').read_text(encoding='utf-8')
info=(r/'docs/软著信息采集表-草稿.md').read_text(encoding='utf-8')
screens={
 '二、': [('01-empty','空白项目原生窗口')],
 '三、': [('16-minimum-window','最小窗口下的原生结果工作表')],
 '四、': [('08-example-project','独立模拟案例项目档案')],
 '五、': [('02-location','封堵位置与恢复工艺')],
 '六、': [('03-condition','测量工况与口径编辑')],
 '七、': [('04-baseline','基线试验工作表'),('05-single','单项封堵试验'),('09-combination','模拟组合封堵记录')],
 '八、': [('10-import-preview','频带CSV预检与覆盖数量')],
 '九、': [('06-pair','显式基线目标配对'),('11-example-difference','模拟案例差值图与数据表'),('12-original-spectrum','基线和目标的原始频谱')],
 '十、': [('13-quality','重复性与恢复等质量检查'),('14-settings','工程筛查参数')],
 '十一、': [('15-review','分析复核和累计审计')],
 '十三、': [('07-manual-result','人工输入70与67dB得到3dB差值')],
 '十四、': [('17-invalid-draft','无效名称草稿被拒绝后保留输入'),('18-blocked-comparison','缺少车辆口径时阻断配对')]
}
figures=[]
def setup(doc):
 sec=doc.sections[0];sec.page_width=Cm(21);sec.page_height=Cm(29.7);sec.top_margin=Cm(2.2);sec.bottom_margin=Cm(2);sec.left_margin=Cm(2.4);sec.right_margin=Cm(2.4)
 style=doc.styles['Normal'];style.font.name='Microsoft YaHei';style._element.rPr.rFonts.set(qn('w:eastAsia'),'微软雅黑');style.font.size=Pt(11);style.paragraph_format.line_spacing=1.35
 sec.header.paragraphs[0].text='车身声学泄漏路径与封堵试验分析软件 V3.0 | Word分页待核'
 p=sec.footer.paragraphs[0];p.text='V3.0 操作资料 · 页 '
 field=OxmlElement('w:fldSimple');field.set(qn('w:instr'),'PAGE');p._p.append(field)
def insert_figures(doc,title):
 for prefix, entries in screens.items():
  if title.startswith(prefix):
   for filename,caption in entries:
    path=r/'docs/screenshots'/f'{filename}.png'
    if not path.exists():continue
    number=len(figures)+1
    doc.add_paragraph(f'本节操作对应图{number}。图中数据为人工填写或固定模拟数列，不能证明实车效果。')
    doc.add_picture(str(path),width=Cm(16.0))
    p=doc.add_paragraph(f'图{number}  {caption}（Windows真实WPF窗口）');p.paragraph_format.keep_with_next=False
    figures.append({'number':number,'file':f'docs/screenshots/{filename}.png','caption':caption,'chapter':title})
def make(text,path,illustrated=False):
 doc=Document();setup(doc);chapter=''
 for block in text.split('\n\n'):
  block=block.strip()
  if not block:continue
  if block.startswith('## '):
   if illustrated:insert_figures(doc,chapter)
   chapter=block[3:];doc.add_heading(chapter,1)
  elif block.startswith('# '):doc.add_heading(block[2:],0)
  elif block.startswith('### '):doc.add_heading(block[4:],2)
  else:doc.add_paragraph(block)
 if illustrated:insert_figures(doc,chapter)
 doc.save(path)
make(manual,r/'docs/操作说明书-待实机核验.docx',True)
make(info,r/'docs/软著信息采集表-草稿.docx')
inventory=json.loads((r/'docs/source-inventory.json').read_text())
doc=Document();setup(doc);doc.add_heading('V3.0生产源码排印稿',0)
doc.add_paragraph('实际构建手写生产文件，排除测试、生成文件、工具和文档；Word实际分页未核。')
for row in inventory['files']:
 doc.add_heading(row['path'],1)
 for line in (r/row['path']).read_text().splitlines():
  p=doc.add_paragraph();p.paragraph_format.space_after=Pt(0);p.paragraph_format.line_spacing=1
  run=p.add_run(line);run.font.name='Consolas';run.font.size=Pt(8)
doc.save(r/'docs/生产源码排印稿-未核分页.docx')
main_function=info.split('软件主要功能',1)[1].split('：',1)[1].split('技术特点',1)[0]
count=len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff]',main_function))
word_file=r/'docs/word-runtime-check.json'
word=json.loads(word_file.read_text(encoding='utf-8-sig')) if word_file.exists() else {}
(r/'docs/material-check.json').write_text(json.dumps({'main_function_han_count':count,'manual_han_count':len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff]',manual)),'word_page_count':word.get('wordPageCount'),'windows_screenshots':len(list((r/'docs/screenshots').glob('*.png'))),'embedded_figures':figures,'status':'windows_illustrated_draft_word_pagination_pending' if figures else 'draft_not_final','dedicated_template_available':False},ensure_ascii=False,indent=2),encoding='utf-8')
print('main_function_han=',count,'figures=',len(figures),'word_pages=',word.get('wordPageCount'))
