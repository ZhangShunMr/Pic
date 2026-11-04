"""Produce editable materials from current source inventory and real screenshots."""
from pathlib import Path
from datetime import datetime,timezone
import re,json,html,textwrap
from PIL import Image
from docx import Document
from docx.shared import Mm,Pt
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from manual_content import PAGES,NAME
ROOT=Path(__file__).resolve().parents[1];DOCS=ROOT/'docs'
FUNCTIONS='''本软件面向汽车空调系统与噪声振动工程设计人员，围绕鼓风机风量挡位转速与结构模态频率之间的交叉关系，提供从项目建立、参数录入、结果复核、方案调整到保存和成果输出的完整工作流程。软件采用模型视图视图模型结构，将工程对象、计算逻辑、文件交换和界面编排分离，提供解决方案启动入口和本机浏览器工作台。用户从空白项目开始，填写名称、负责人、车辆平台、设计目标、来源类别及来源说明，可打开本机项目库、复制独立项目、归档和解除归档，项目修订与关键操作通过审计记录追溯。内置示例采用确定性的模拟设计参数，界面、项目和成果持续保留模拟标记，不能当作真实试验结果。
软件支持管理轴旋转阶次和叶片通过倍频，依据叶片数与倍数计算有效阶次，允许启用与停用并防止重复有效阶次被计数。结构模态管理记录频率、可选阻尼比、附加敏感半宽、安装位置、来源类别与测试或仿真边界。阻尼未知与明确为零分别保存。用户可建立多个挡位方案，编辑按序递增的目标转速，设定扫描固定挡位，锁定方案以防误改，复制方案继续设计。所有对象均经过数值范围、单位、顺序、间距、数量及稳定标识检查。
核心计算把有效阶次与转速换算成激励频率，计算阶次与模态的精确交叉转速，按绝对半宽、百分比半宽、可选阻尼近似半宽和模态附加半宽的最大值建立敏感区间，再把转速容差换算为激励频率区间。两个区间接触或重叠时标记潜在接近，保留频率上下界、净距和规则分项依据。常驻转速频率图显示阶次线、模态带和挡位虚线，图、对象树与右侧属性联动；用户可调纵轴范围，在底部搜索和筛选全部组合或接近组合，并核对分析范围内外的交叉。
约束扫描在每个挡位原转速附近建立离散网格，保持固定挡位并满足相邻最小间距，通过动态规划选择接近组合数与归一化转速偏移代价之和较小的候选。扫描先预览，采纳时新增独立方案，不覆盖原方案；计算输入变化后旧扫描不能直接采用。方案比较共用当前模态、阶次和规则，按挡位序号显示转速差、接近挡位数和组合数；挡位数量不同时保留缺失并提示比较边界。规则敏感性在输入副本上试算不同绝对半宽和转速容差组合，显示相对原规则的状态变化，不修改工作项目。质量复核提示模拟来源、未知阻尼、模态敏感区重叠、来源差异、缺少边界和需复核证据等事项。
软件可以从潜在接近组合生成不重复的验证清单，记录验证方法、负责人、状态、证据位置与更新时间。计算输入改变后完成记录自动转为需复核，被验证记录引用的对象不能直接删除。会话支持撤销重做并保留累计审计，属性草稿应用前独立存在，切换或替换工作时提示未保存内容。批量输入采用明确表头的逗号分隔文件，支持文件载入和文本编辑，预检列出错误行、解析对象与字段原因；应用前复查内容和输入身份，任一错误整批拒绝，挡位新增独立方案、模态追加。
项目文件具有格式版本，内嵌全部输入、来源、方案、验证和审计，支持完整保存、重新打开和搬迁导入。保存使用临时写入、刷新、备份和替换，检测其他会话修改冲突，读取时拒绝损坏格式、未知字段、重复字段、无效引用和不支持版本。输出包括可重新打开的项目、包含计算口径和矢量图的自包含设计报告、频率接近明细、交叉转速、验证清单、规则敏感性和矢量设计图。成果携带版本、单位、参数、来源和输入哈希。软件只筛查频率位置，不处理实测波形，不预测声压、响应幅值、风量或效率；阻尼带宽只作为轻阻尼孤立模态近似，频率接近不构成真实共振或降噪证明，候选方案仍须台架及整车验证。'''

def style(doc,kind):
 sec=doc.sections[0];sec.page_width=Mm(210);sec.page_height=Mm(297)
 sec.top_margin=Mm(18);sec.bottom_margin=Mm(18);sec.left_margin=Mm(20);sec.right_margin=Mm(20)
 for name in ['Normal','Title','Heading 1','Heading 2','Caption']:
  st=doc.styles[name];st.font.name='Noto Sans CJK SC';st.element.get_or_add_rPr().rFonts.set(qn('w:eastAsia'),'Noto Sans CJK SC')
 normal=doc.styles['Normal'];normal.font.size=Pt(10.5);normal.paragraph_format.line_spacing=1.35;normal.paragraph_format.space_after=Pt(7)
 doc.styles['Heading 1'].font.size=Pt(17);doc.styles['Caption'].font.size=Pt(8.5)
 sec.header.paragraphs[0].text=NAME+' V1.0 · '+kind
 sec.header.paragraphs[0].style=doc.styles['Caption']
 footer=sec.footer.paragraphs[0];footer.alignment=2;footer.add_run('第 ')
 fld=OxmlElement('w:fldSimple');fld.set(qn('w:instr'),'PAGE');footer._p.append(fld);footer.add_run(' 页 · V1.0')
 doc.core_properties.author='';doc.core_properties.last_modified_by='';doc.core_properties.title=NAME+' V1.0 '+kind
 doc.core_properties.created=datetime.now(timezone.utc);doc.core_properties.modified=datetime.now(timezone.utc)

def manual():
 doc=Document();style(doc,'操作说明书');md=['# '+NAME+' V1.0 操作说明书',''];figures=[];sheets=[]
 for index,(title,paragraphs,filename) in enumerate(PAGES):
  if index:doc.add_page_break()
  doc.add_heading(title,1);md+=['## '+title,''];parts=['<section><h1>'+html.escape(title)+'</h1>']
  for text in paragraphs:
   doc.add_paragraph(text);md+=[text,''];parts.append('<p>'+html.escape(text)+'</p>')
  if filename:
   image=DOCS/'screenshots'/filename;assert image.is_file(),image
   with Image.open(image) as im:w,h=im.size
   # Fit actual task capture; narrow inspector captures remain large enough to read.
   width=min(166,130*w/h);height=width*h/w
   doc.add_picture(str(image),width=Mm(width))
   cap='图'+str(len(figures)+1)+' '+title+'（真实运行界面；模拟设计数据）'
   doc.add_paragraph(cap,style='Caption');md+=['!['+cap+'](screenshots/'+filename+')','']
   figures.append({'figure':len(figures)+1,'file':filename,'caption':cap,'widthMm':round(width,1),'heightMm':round(height,1)})
   parts.append('<figure><img style="width:'+str(width)+'mm" src="screenshots/'+html.escape(filename)+'"><figcaption>'+html.escape(cap)+'</figcaption></figure>')
  parts.append('<footer>'+NAME+' V1.0 · '+str(index+1)+'</footer></section>');sheets.append(''.join(parts))
 doc.save(DOCS/'操作说明书.docx');(DOCS/'操作说明书.md').write_text('\n'.join(md))
 css='@page{size:A4;margin:0}*{box-sizing:border-box}body{font-family:"Noto Sans CJK SC",sans-serif;color:#303137;margin:0}section{width:210mm;min-height:297mm;padding:18mm 20mm;break-after:page;position:relative}h1{font-size:17pt;border-bottom:2px solid #bd511d;padding-bottom:8px;margin:0 0 12px}p{font-size:10.5pt;line-height:1.6;margin:0 0 10px;text-align:justify}figure{margin:12px 0}img{max-width:166mm;display:block}figcaption{font-size:8.5pt;color:#827360}footer{font-size:8pt;margin-top:12px;color:#827360}'
 (DOCS/'操作说明书-浏览器预览.html').write_text('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>操作说明书预览</title><style>'+css+'</style>'+''.join(sheets)+'</html>')
 return figures

def information():
 count=len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff]',FUNCTIONS));assert count>500
 inventory=json.loads((DOCS/'source-inventory.json').read_text())
 rows=[('软件全称',NAME),('版本号','V1.0；内部版本1.0.0；项目格式schema1'),('简称','待申请人核实/填写'),('申请人、国籍、证件、联系方式','待申请人核实/填写'),('著作权人、权属来源、开发方式','待申请人核实/填写'),('开发完成日期','待申请人核实/填写；不以材料生成日代替'),('首次发表状态、日期、地点','待申请人核实/填写；不以仓库推送自动认定'),('开发语言和技术','C#、Razor、CSS、少量JavaScript；MVVM；BlowerResonance.sln入口'),('运行平台','.NET10 ASP.NET Core本机服务及现代浏览器；Linux/Chromium已实跑；Windows和Visual Studio图形启动未验证'),('开发环境','.NET SDK10.0.403；运行时10.0.14；生产仅微软框架，无额外业务NuGet依赖'),('主要功能',FUNCTIONS),('源程序量',str(inventory['productionNonblank'])+'手写生产非空行，其中C# '+str(inventory['byExtension']['.cs'])+'行；排除测试、工具、依赖、文档和生成文件'),('数据格式','.blowerproj（JSON/schema1），CSV、HTML、SVG；项目全部输入内嵌'),('技术特点','常驻对象树/交互设计图/选中属性/结果核对；区间交叉筛查、动态规划离散扫描、规则敏感性、审计与冲突保存'),('模板与提交范围','未提供专用申报模板；使用仓库已有材料的A4、标题层级、页眉与图注作为版式参考。正式字段顺序、表格合并与源程序提交页数待申请人按当次模板复核。'),('分页状态','操作说明书为可编辑稿；本环境不具备Microsoft Word，Word实际分页仍待核实。另附实际渲染预览及逐页检查证据。')]
 doc=Document();style(doc,'软著信息采集表');doc.add_heading('软著信息采集表（申请人复核稿）',1)
 doc.add_paragraph('主要功能按Unicode汉字计数为'+str(count)+'字；身份、权属和日期未经核实，不代填。')
 table=doc.add_table(rows=0,cols=2);table.style='Table Grid'
 for label,value in rows:
  cells=table.add_row().cells;cells[0].text=label;cells[1].text=value
  # Permit the long functions cell to continue across pages, preserving border and text.
 table.columns[0].width=Mm(35);table.columns[1].width=Mm(131)
 doc.save(DOCS/'软著信息采集表.docx')
 (DOCS/'软著信息采集表.md').write_text('# 软著信息采集表\n\n主要功能Unicode汉字：'+str(count)+'字。\n\n'+'\n\n'.join('## '+k+'\n\n'+v for k,v in rows))
 return count

def source_document():
 inventory=json.loads((DOCS/'source-inventory.json').read_text());doc=Document();style(doc,'完整生产源码打印稿')
 sec=doc.sections[0];sec.left_margin=Mm(15);sec.right_margin=Mm(15)
 normal=doc.styles['Normal'];normal.font.name='Noto Sans Mono CJK SC';normal.font.size=Pt(7.5);normal.paragraph_format.line_spacing=1;normal.paragraph_format.space_after=Pt(0)
 visual=[]
 for file in inventory['files']:
  visual.append('文件：'+file['path'])
  for number,line in enumerate((ROOT/file['path']).read_text().splitlines(),1):
   chunks=textwrap.wrap(line.expandtabs(4),width=110,replace_whitespace=False,drop_whitespace=False) or ['']
   for i,chunk in enumerate(chunks):visual.append((f'{number:04d} ' if i==0 else '     ')+chunk)
 for i in range(0,len(visual),64):
  if i:doc.add_page_break()
  doc.add_heading('完整生产源码 · '+str(i//64+1),2)
  for line in visual[i:i+64]:doc.add_paragraph(line)
 doc.save(DOCS/'完整生产源码打印稿.docx')
 return len(visual)

if __name__=='__main__':
 figures=manual();count=information();lines=source_document()
 evidence={'generatedUtc':datetime.now(timezone.utc).isoformat(),'manualSections':len(PAGES),'figures':figures,'functionsHanCount':count,'sourceVisualLines':lines,'template':'仓库既有A4手册版式参考；无申请人专用模板','wordPagination':'pending: Microsoft Word not available; page breaks are not pagination evidence'}
 (DOCS/'material-build.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2));print(json.dumps({k:v for k,v in evidence.items() if k!='figures'},ensure_ascii=False))
