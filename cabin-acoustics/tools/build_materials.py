from pathlib import Path
import json,re,html,base64
from docx import Document
from docx.shared import Cm,Pt
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from manual_content import NAME,CHAPTERS,FUNCTIONS
ROOT=Path(__file__).resolve().parents[1]
DOCS=ROOT/'docs'
TEMPLATE=ROOT.parent/'acoustic-seal-analysis/docs/操作说明书.docx'
def fresh(template=None):
 d=Document(template) if template else Document()
 if template:
  body=d._element.body
  for c in list(body):
   if c.tag!=qn('w:sectPr'):body.remove(c)
 s=d.sections[0]
 s.page_width=Cm(21);s.page_height=Cm(29.7)
 s.top_margin=s.bottom_margin=Cm(1.8);s.left_margin=s.right_margin=Cm(2)
 for style_name in ['Normal','Body Text']:
  style=d.styles[style_name];style.font.name='Microsoft YaHei';style.font.size=Pt(10.5)
  style._element.rPr.rFonts.set(qn('w:eastAsia'),'Microsoft YaHei')
  style.paragraph_format.space_after=Pt(8);style.paragraph_format.line_spacing=1.45
 for style_name,size in [('Heading 1',19),('Heading 2',14)]:
  d.styles[style_name].font.name='Microsoft YaHei';d.styles[style_name].font.size=Pt(size)
  d.styles[style_name]._element.rPr.rFonts.set(qn('w:eastAsia'),'Microsoft YaHei')
 s.header.paragraphs[0].text=NAME+' V1.0 · 操作说明书'
 for p in s.footer.paragraphs:p.text=''
 foot=s.footer.paragraphs[0];foot.alignment=2
 r=foot.add_run('V1.0 / ');r.font.size=Pt(8)
 f=OxmlElement('w:fldSimple');f.set(qn('w:instr'),'PAGE');foot._p.append(f)
 d.core_properties.title=NAME+' V1.0'
 d.core_properties.author='待申请人核实'
 return d
manual=fresh(TEMPLATE)
html_pages=[];markdown=['# '+NAME+' V1.0 操作说明书','']
figure=0
for i,(title,shot,paragraphs) in enumerate(CHAPTERS):
 if i:manual.add_page_break()
 manual.add_heading(title,level=1)
 markdown.extend(['## '+title,''])
 page=['<section class="page"><header>'+html.escape(NAME)+' V1.0</header><h1>'+html.escape(title)+'</h1>']
 for text in paragraphs:
  manual.add_paragraph(text)
  markdown.extend([text,''])
  page.append('<p>'+html.escape(text)+'</p>')
 if shot:
  from PIL import Image
  path=DOCS/'screenshots'/shot
  figure+=1
  w,h=Image.open(path).size
  maxw=16.8;maxh=10.0
  width=min(maxw,maxh*w/h)
  p=manual.add_paragraph();p.alignment=1
  p.add_run().add_picture(str(path),width=Cm(width))
  caption=f'图{figure} {Path(shot).stem[3:]}（真实运行截图；模拟内容不是实测）'
  p=manual.add_paragraph(caption);p.alignment=1
  for r in p.runs:r.font.size=Pt(8)
  markdown.extend([f'![{caption}](screenshots/{shot})',''])
  encoded=base64.b64encode(path.read_bytes()).decode()
  page.append(f'<figure><img src="data:image/png;base64,{encoded}"><figcaption>{html.escape(caption)}</figcaption></figure>')
 page.append(f'<footer>操作说明书 · 模拟数据均非实测 / {i+1}</footer></section>')
 html_pages.append(''.join(page))
manual.save(DOCS/'操作说明书.docx')
(DOCS/'操作说明书.md').write_text('\n'.join(markdown))
style='''@page{size:A4;margin:0}*{box-sizing:border-box}body{margin:0;color:#233e41;font-family:"Noto Sans CJK SC",sans-serif}.page{width:210mm;min-height:297mm;position:relative;padding:18mm 20mm 20mm;break-after:page}.page:last-child{break-after:auto}header{font-size:8pt;color:#6a7f72;border-bottom:1px solid #b9cabe;padding-bottom:3mm}h1{font-size:18pt;font-weight:500;margin:7mm 0 6mm}p{font-size:10.3pt;line-height:1.75;text-align:justify;margin:0 0 3.5mm}figure{margin:4mm 0 0;text-align:center;break-inside:avoid}img{max-width:169mm;max-height:102mm;object-fit:contain}figcaption{font-size:8pt;color:#587267;margin-top:2mm}footer{position:absolute;bottom:11mm;right:20mm;font-size:8pt;color:#6a7f72}'''
(DOCS/'操作说明书-预览.html').write_text('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>操作说明书排版预览</title><style>'+style+'</style>'+''.join(html_pages)+'</html>')
count=len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff\U00020000-\U0002a6df]',FUNCTIONS))
fields=[('软件全称',NAME),('版本号','V1.0'),('软件简称','待申请人核实/填写'),('软件类别','应用软件；工程声学简化模型研究工具'),('开发语言','C#；界面Razor、CSS及少量JavaScript'),('开发环境','.NET SDK 10.0.403，Linux Debian 13；源码入口CabinAcoustics.sln'),('运行环境','.NET 10 ASP.NET Core Runtime或SDK；本机现代浏览器。当前实测Linux Chromium，Windows启动待核验。'),('架构','MVVM，Blazor Server；核心计算与文件处理独立于界面'),('文件格式','.cabin.json项目；UTF-8 CSV交换；内嵌SVG的HTML报告'),('软件主要功能',FUNCTIONS),('技术特点','刚性矩形腔模态枚举、退化阶次保留、源与耳点耦合、多源复数相位叠加、共同参考相对响应、剖面联动、冻结基准和单参数扫描。'),('生产源码规模','见source-inventory.json；手写生产源码非空行，排除测试与生成产物。'),('申请人、著作权人','待申请人核实/填写'),('权属及取得方式','待申请人核实/填写'),('开发完成日期','待申请人核实/填写'),('首次发表日期及地点','待申请人核实/填写'),('申报模板及正式源码范围','未提供专用模板；正式提交的字段、页数与源码选取范围待按当次申报要求核定。')]
collect=fresh();collect.sections[0].header.paragraphs[0].text=NAME+' V1.0 · 信息采集表'
collect.add_heading('软著信息采集表',0)
t=collect.add_table(rows=0,cols=2);t.style='Table Grid'
for key,value in fields:
 cells=t.add_row().cells;cells[0].text=key;cells[1].text=value
 for cell in cells:
  for p in cell.paragraphs:
   p.paragraph_format.space_after=Pt(5);p.paragraph_format.line_spacing=1.3
   for r in p.runs:r.font.size=Pt(9.5)
collect.add_paragraph(f'“主要功能”Unicode汉字数：{count}，超过500。申请事实未从示例继承；提交前由申请人核实。')
collect.save(DOCS/'软著信息采集表.docx')
(DOCS/'软著信息采集表.md').write_text('# 软著信息采集表\n\n'+'\n\n'.join('## '+k+'\n\n'+v for k,v in fields)+f'\n\n主要功能Unicode汉字数：{count}。\n')
(DOCS/'material-build.json').write_text(json.dumps({'chapters':len(CHAPTERS),'manualFigures':figure,'mainFunctionHan':count,'template':str(TEMPLATE.relative_to(ROOT.parent)),'wordPagination':'未验证；本环境没有Microsoft Word，浏览器PDF不视为Word实际分页','applicantTemplate':'未提供专用模板'},ensure_ascii=False,indent=2))
print(json.dumps({'chapters':len(CHAPTERS),'figures':figure,'han':count},ensure_ascii=False))
