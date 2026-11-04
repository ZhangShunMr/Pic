from pathlib import Path
from datetime import datetime, timezone
import json,re,html,subprocess
from docx import Document
from docx.shared import Inches,Pt,Cm,RGBColor
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from manual_content import PAGES, MAIN_FUNCTIONS
ROOT=Path(__file__).resolve().parents[1]
DOCS=ROOT/'docs'
NAME='汽车附件电机电磁激励频率设计软件 V1.0'

def setup(document,title):
    sec=document.sections[0]
    sec.page_width=Cm(21);sec.page_height=Cm(29.7)
    sec.top_margin=Cm(2.1);sec.bottom_margin=Cm(2)
    sec.left_margin=Cm(2.1);sec.right_margin=Cm(2.1)
    sec.header_distance=Cm(.8);sec.footer_distance=Cm(.9)
    normal=document.styles['Normal']
    normal.font.name='Noto Sans CJK SC';normal.font.size=Pt(10.5)
    normal._element.rPr.rFonts.set(qn('w:eastAsia'),'Noto Sans CJK SC')
    normal.paragraph_format.line_spacing=1.35
    normal.paragraph_format.space_after=Pt(8)
    for name,size in [('Heading 1',18),('Heading 2',14),('Heading 3',12)]:
        st=document.styles[name];st.font.name='Noto Sans CJK SC';st.font.size=Pt(size)
        st._element.rPr.rFonts.set(qn('w:eastAsia'),'Noto Sans CJK SC')
        st.font.color.rgb=RGBColor.from_string('1D6860')
    header=sec.header.paragraphs[0]
    header.text=NAME+' | '+title
    header.style='Caption';header.runs[0].font.size=Pt(8)
    footer=sec.footer.paragraphs[0];footer.alignment=2
    footer.add_run('材料预览 · Word实际分页待核实 | 第 ')
    field=OxmlElement('w:fldSimple');field.set(qn('w:instr'),'PAGE');footer._p.append(field)
    footer.add_run(' 页')

def prevent_row_split(row):
    prop=row._tr.get_or_add_trPr();node=OxmlElement('w:cantSplit');prop.append(node)

manual=Document();setup(manual,'操作说明书')
md=['# '+NAME+' 操作说明书','','本稿与当前真实界面对应。PDF为LibreOffice预览；Word实际分页与申请人模板待核实。','']
figure=0
for index,(title,screenshot,paras) in enumerate(PAGES):
    heading=manual.add_heading(f'{index+1}. {title}',level=1)
    if index: heading.paragraph_format.page_break_before=True
    md += [f'## {index+1}. {title}','']
    for text in paras:
        manual.add_paragraph(text);md += [text,'']
    if screenshot:
        path=DOCS/'screenshots'/screenshot
        if not path.exists(): raise RuntimeError('缺少真实截图 '+str(path))
        figure+=1
        manual.add_paragraph(f"操作位置和当前界面状态见图 {figure}。")
        md += [f"操作位置和当前界面状态见图 {figure}。", ""]
        manual.add_picture(str(path),width=Cm(16.4))
        caption=f'图 {figure}  {title}（真实运行界面；含模拟案例资料）'
        p=manual.add_paragraph(caption,style='Caption');p.alignment=1
        md += [f'![{caption}](screenshots/{screenshot})','']
manual.save(DOCS/'操作说明书.docx')
(DOCS/'操作说明书.md').write_text('\n'.join(md))

han=len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff]',MAIN_FUNCTIONS))
assert han>500
fields=[
 ('软件全称','汽车附件电机电磁激励频率设计软件'),('版本号','V1.0'),
 ('软件简称','待申请人核实/填写'),('申请人、著作权人','待申请人核实/填写'),
 ('权利取得方式、开发方式','待申请人核实/填写'),('开发完成日期、首次发表日期及地点','待申请人核实/填写'),
 ('运行平台','支持 .NET 10 的本机环境，浏览器承载 Blazor Server 界面；实际验证为本次 Linux / Chromium。Windows、macOS未实机验证。'),
 ('开发语言与模式','C#，MVVM；视图为Razor/SVG/CSS，少量JS仅文件下载和离开提示。'),
 ('运行环境','安装 .NET SDK 10.0.100 或允许的后续SDK。入口 MotorFrequency.sln；默认 http://127.0.0.1:8774，仅回环监听。无生产第三方NuGet依赖。'),
 ('项目格式及数据','UTF-8 .motorproj JSON，格式版本1，频谱原始点内嵌；双列CSV导入，CSV/SVG/HTML/JSON导出。'),
 ('开发硬件及正式运行硬件要求','开发硬件、正式最低硬件要求及登记字段待申请人核实/填写；未做最大规模性能认证。'),
 ('软件主要功能',MAIN_FUNCTIONS),
 ('技术特点','C#核心算法和格式校验与界面分离；MVVM管理输入草稿、命令和结果。电机结构对象联动频率图与条件检查器，区分无刷同步电气周期和有刷单刷片通过参考，理想齿槽候选需确认假设。解析频段求交、同频候选分别保留、嵌入原始频点、带哈希追溯、备份与冲突阻断。'),
 ('源码规模','见source-inventory.json与源码清单；手写生产C#/Razor/CSS/必要JS非空行，排除测试工具及生成物。'),
 ('材料状态','当前实现材料；截图来自真实Chromium运行；申请人模板、Microsoft Word实际分页与登记事实待核实。')]
info=Document();setup(info,'软著信息采集表')
info.add_heading('软件著作权信息采集表',level=1)
info.add_paragraph('按当前可运行版本填写；尚无申请人专用模板，使用可编辑A4表格。权属和日期不继承其他软件示例。')
table=info.add_table(rows=0,cols=2);table.style='Table Grid'
table.autofit=False
table.columns[0].width=Cm(3.5)
table.columns[1].width=Cm(13.3)
for key,value in fields:
 row=table.add_row();row.cells[0].width=Cm(3.5);row.cells[1].width=Cm(13.3)
 row.cells[0].text=key;row.cells[1].text=value
 if key!='软件主要功能':prevent_row_split(row)
info.save(DOCS/'软著信息采集表.docx')
(DOCS/'软著信息采集表.md').write_text('# 软件著作权信息采集表\n\n'+'\n\n'.join('## '+k+'\n\n'+v for k,v in fields)+f'\n\n主要功能Unicode汉字数：{han}。\n')

source=Document();setup(source,'完整生产源码排印稿')
source.add_heading('完整生产源码排印稿',level=1)
source.add_paragraph('实际参与构建的手写生产源码；正式登记截取范围、页数和行格式按当次申报要求核定。完整清单为可检索文本，不包含测试及工具。')
inv=json.loads((DOCS/'source-inventory.json').read_text())
style=source.styles.add_style('SourceCode',1)
style.font.name='Noto Sans Mono CJK SC';style.font.size=Pt(7)
style._element.get_or_add_rPr().rFonts.set(qn('w:eastAsia'),'Noto Sans Mono CJK SC')
style.paragraph_format.line_spacing=1
style.paragraph_format.space_after=Pt(0)
for item in inv['files']:
    source.add_heading(item['file'],level=2)
    for line in (ROOT/item['file']).read_text().splitlines(): source.add_paragraph(line or ' ',style='SourceCode')
source.save(DOCS/'完整生产源码排印稿.docx')

preview=DOCS/'pdf-preview';preview.mkdir(exist_ok=True)
command=['soffice','-env:UserInstallation=file:///tmp/motor-docx-profile','--headless','--convert-to','pdf','--outdir',str(preview),str(DOCS/'操作说明书.docx'),str(DOCS/'软著信息采集表.docx')]
converted=subprocess.run(command,capture_output=True,text=True,timeout=120)
(DOCS/'document-conversion-log.txt').write_text(converted.stdout+'\n'+converted.stderr)
if converted.returncode:raise RuntimeError(converted.stderr)
import fitz
pdf=fitz.open(preview/'操作说明书.pdf')
pages=[]
thumbs=[]
for i,page in enumerate(pdf):
    text=page.get_text()
    pages.append({'page':i+1,'textCharacters':len(text),'images':len(page.get_image_info()),'hasHeader':'V1.0' in text,'hasFooter':'Word' in text})
    assert len(text)>80,(i+1,'页面有效文字过少')
    pix=page.get_pixmap(matrix=fitz.Matrix(.5,.5))
    from PIL import Image,ImageDraw
    thumb=Image.frombytes('RGB',[pix.width,pix.height],pix.samples)
    canvas=Image.new('RGB',(thumb.width,thumb.height+25),'white');canvas.paste(thumb,(0,25));ImageDraw.Draw(canvas).text((8,6),f'PAGE {i+1}',fill='black')
    thumbs.append(canvas)
assert len(pdf)>=10
columns=4
rows=(len(thumbs)+columns-1)//columns
w=max(x.width for x in thumbs);h=max(x.height for x in thumbs)
contact=Image.new('RGB',(columns*w,rows*h),'#dddddd')
for i,thumb in enumerate(thumbs): contact.paste(thumb,((i%columns)*w,(i//columns)*h))
contact.save(DOCS/'操作手册逐页预览.png')
(DOCS/'material-evidence.json').write_text(json.dumps({'at':datetime.now(timezone.utc).isoformat(),'manualSections':len(PAGES),'manualPdfPages':len(pdf),'paginationEngine':'LibreOfficeDev 26.8.0.0.alpha0','wordPaginationVerified':False,'applicantTemplateProvided':False,'mainFunctionHanCount':han,'figures':figure,'pages':pages,'remaining':['Microsoft Word实际分页至少10页终稿验收','申请人专用模板核对','申请人身份权属及开发发表事实']},ensure_ascii=False,indent=2))
print(json.dumps({'manualPdfPages':len(pdf),'han':han,'figures':figure,'wordPaginationVerified':False},ensure_ascii=False))
