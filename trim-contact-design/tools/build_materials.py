from pathlib import Path
import re,json,hashlib
from docx import Document
from docx.shared import Cm,Pt,RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from PIL import Image
from manual_content import SECTIONS,FUNCTIONS
ROOT=Path(__file__).resolve().parents[1]
DOCS=ROOT/'docs'
NAME='汽车内饰卡扣与防异响接触设计软件'
VERSION='V1.0'

def style_document(doc,kind,code=False):
 s=doc.sections[0]
 s.page_width=Cm(21);s.page_height=Cm(29.7)
 s.top_margin=Cm(1.8);s.bottom_margin=Cm(1.8);s.left_margin=Cm(2);s.right_margin=Cm(2)
 s.header_distance=Cm(.8);s.footer_distance=Cm(.8)
 for name,size in [('Normal',7 if code else 10.5),('Heading 1',17),('Heading 2',13),('Caption',9)]:
  st=doc.styles[name];st.font.name='Noto Sans CJK SC';st.font.size=Pt(size)
  st.element.get_or_add_rPr().rFonts.set(qn('w:eastAsia'),'Noto Sans CJK SC')
  if name=='Normal':
   st.paragraph_format.space_after=Pt(7 if not code else 0)
   st.paragraph_format.line_spacing=1.25 if not code else 1.0
  if name.startswith('Heading'):
   st.font.color.rgb=RGBColor.from_string('235D52')
   st.paragraph_format.space_before=Pt(4);st.paragraph_format.space_after=Pt(10)
 hp=s.header.paragraphs[0];hp.text=NAME+' '+VERSION+' · '+kind
 hp.style=doc.styles['Caption']
 fp=s.footer.paragraphs[0];fp.alignment=WD_ALIGN_PARAGRAPH.RIGHT
 fp.add_run('第 ')
 field=OxmlElement('w:fldSimple');field.set(qn('w:instr'),'PAGE');fp._p.append(field)
 fp.add_run(' 页 · 内部复核稿')
 doc.core_properties.title=NAME+' '+VERSION+' '+kind
 doc.core_properties.author='待申请人核实/填写'
 doc.core_properties.subject='实际运行版本的操作与材料复核稿'
 doc.core_properties.comments='参照仓库既有A4文档样式；专用模板和Word分页待申请人核对。'

def paragraph(doc,label,text):
 p=doc.add_paragraph();p.add_run(label+'：').bold=True;p.add_run(text)
 return p

manual=Document();style_document(manual,'操作说明书')
figure=0
manual_text=['# '+NAME+' '+VERSION+' 操作说明书','', '本稿待Microsoft Word实际分页及专用模板复核。','']
for i,(title,shot,paras) in enumerate(SECTIONS,1):
 if i>1: manual.add_page_break()
 manual.add_heading(f'{i} {title}',level=1)
 manual_text.extend(['## '+str(i)+' '+title,''])
 for label,text in paras:
  paragraph(manual,label,text)
  manual_text.extend([f'**{label}：**{text}',''])
 if shot:
  image_path=DOCS/'screenshots'/(shot+'.png')
  assert image_path.exists(),image_path
  figure+=1
  w,h=Image.open(image_path).size
  width=min(17,10.8*w/h)
  p=manual.add_paragraph();p.alignment=WD_ALIGN_PARAGRAPH.CENTER
  p.paragraph_format.line_spacing=1.0;p.paragraph_format.space_after=Pt(2)
  p.add_run().add_picture(str(image_path),width=Cm(width))
  cap=manual.add_paragraph(f'图{figure} {title}的实际运行页面（模拟案例）',style='Caption');cap.alignment=WD_ALIGN_PARAGRAPH.CENTER
  manual_text.extend([f'![图{figure} {title}](screenshots/{shot}.png)',''])
manual.save(DOCS/'操作说明书.docx')
(DOCS/'操作说明书.md').write_text('\n'.join(manual_text).rstrip()+'\n')

count=sum('\u3400'<=c<='\u4dbf' or '\u4e00'<=c<='\u9fff' or '\U00020000'<=c<='\U0002fa1f' for c in FUNCTIONS)
assert count>500,count
inventory=json.loads((DOCS/'source-inventory.json').read_text())
fields=[
('软件全称',NAME),('版本号','V1.0；项目schema 1；模型RigidContact-1.0'),
('软件简称','待申请人核实/填写（程序未声明简称）'),
('申请人、国籍、证件和联系方式','待申请人核实/填写'),
('著作权人、权属来源及开发方式','待申请人核实/填写'),
('开发完成日期','待申请人核实/填写；不以本次生成或验证日期代替'),
('首次发表状态、日期、地点','待申请人核实/填写；代码推送与法定发表事实分别核实'),
('语言与工程','C#、Razor视图及CSS样式；TrimContact.sln；MVVM；本机Blazor Server应用'),
('运行环境','.NET10/ASP.NET Core与现代浏览器；Linux/Chromium已运行；Windows/macOS启动待实机验证'),
('开发工具与依赖','.NET SDK10.0.403；生产无第三方NuGet依赖；Python材料工具为可选开发工具'),
('主要功能',FUNCTIONS),
('源程序量',f"{inventory['nonblank']}行手写生产C#与Razor非空行；测试、生成、工具、文档、样例、CSS和JS不计最低门槛。逐文件SHA256见源码清单。"),
('文件格式','.trimproj（schema 1）；布置CSV导入导出；结果HTML、CSV及分析JSON；输入最大2MB'),
('模板与申报范围','参照仓库已有采集表字段顺序和A4版式，未收到申请人专用模板；当次合并单元格、签章、正式源码范围与页数待核实')]
info=Document();style_document(info,'软著信息采集表')
info.add_heading('软著信息采集表（申请人复核稿）',1)
info.add_paragraph(f'“主要功能”按Unicode汉字计数为{count}字。身份、权属和日期未代填。')
table=info.add_table(rows=0,cols=2);table.style='Table Grid';table.autofit=False
table.columns[0].width=Cm(3.7);table.columns[1].width=Cm(13.3)
for key,value in fields:
 cells=table.add_row().cells;cells[0].width=Cm(3.7);cells[1].width=Cm(13.3)
 cells[0].text=key;cells[1].text=value
 for p in cells[0].paragraphs:
  for r in p.runs:r.bold=True
 for cell in cells:
  for p in cell.paragraphs:p.paragraph_format.space_after=Pt(5)
info.save(DOCS/'软著信息采集表.docx')
(DOCS/'软著信息采集表.md').write_text('# 软著信息采集表（申请人复核稿）\n\n'+f'主要功能Unicode汉字：{count}字。\n\n'+'\n\n'.join(f'## {k}\n\n{v}' for k,v in fields)+'\n')

code=Document();style_document(code,'完整生产源码排印稿',True)
for index,e in enumerate(inventory['files']):
 if index:code.add_page_break()
 p=code.add_paragraph(e['path']);p.paragraph_format.keep_with_next=True
 for num,line in enumerate((ROOT/e['path']).read_text().splitlines(),1):
  p=code.add_paragraph(f'{num:04d}  {line}')
  p.paragraph_format.space_after=Pt(0)
code.save(DOCS/'完整生产源码排印稿.docx')
manifest={'manual_sections':len(SECTIONS),'unique_figures':figure,'major_function_han_count':count,
 'production_nonblank':inventory['nonblank'],'word_pagination':'NOT VERIFIED; requires Microsoft Word',
 'template':'Repository A4 style and field-order reference; applicant-specific template not supplied'}
(DOCS/'materials-check.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2))
print(json.dumps(manifest,ensure_ascii=False))
