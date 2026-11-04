"""Generate editable materials from current operation text and browser evidence."""
import json,re
from pathlib import Path
from datetime import datetime,timezone
from docx import Document
from docx.shared import Mm,Pt,RGBColor
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from PIL import Image
ROOT=Path(__file__).resolve().parents[1];DOCS=ROOT/'docs'
NAME='车外行人警示声音传播与覆盖设计软件'
PAGES=json.loads((ROOT/'tools/manual-content.json').read_text())
FUNCTIONS='''本软件面向汽车声学、附件开发和车辆布置工程人员，用于组织车外行人警示扬声器的方案输入，在自由声场近似下计算频带声压分布、比较工程目标覆盖并制作相对声音试听。项目管理从空白工作对象开始，记录名称、负责人、车辆代号、设计目的和备注，支持独立项目复制、只读归档与解除归档，保留稳定项目标识和递增修订。模拟双扬声器示例由单独入口载入，来源标记在界面、项目与工程报告中持续保留，不能当作真实车辆数据。
扬声器管理支持最多八个源，分别维护启用状态、三维坐标、水平朝向、轴向参考距离、全频带增益、指向指数与背向幅值下限，维护七个倍频程中心频率的参考声压级和独立频响修正。来源类别区分人工输入、模拟示例、实测用户声明和估算，并记录来源和参考条件。背景管理独立保存七频带和来源，定义空间均匀背景假设；接收平面设置范围、步长、统一高度、距离截断、车辆几何轮廓及内部节点排除方式，明确节点数量限制和输入范围。
输入既可逐字段录入，也可使用频谱文件预检整体导入。软件检查表头、编码、列数、有限数值、频率完整性、重复和缺失，逐行报告错误，并拒绝部分覆盖。观察点可逐点建立、删除和编辑，也可按车辆中心生成等角环形点集，或通过支持引号转义的四列文件批量追加与替换。观察点交换与完整项目搬迁分别处理，重建点不冒充已有点标识。字段应用、项目保存和结果状态分别显示，未应用字段会阻止依赖操作，参数变化后旧计算结果失效。
核心处理按三维距离执行球面几何扩散，叠加水平指向性、参考频带、修正和增益，多源逐带使用非相干能量叠加。软件分别提供单频带、七带非计权合成和七带中心频率近似计权指标，计算警示声、背景、能量合成、背景差和用户工程目标状态。色图显示离散网格声场与二值目标覆盖，观察点表格联动频谱和各源距离及传播修正，近源截断、车内示意和网格外点有单独提示。覆盖率是有效节点比例，不能解释为人群可听率或连续面积积分。
方案快照冻结布局、背景、频带和观察点，比较前核对区域、步长、接收高度、目标、指标、来源类别和观察点标识及坐标，条件不一致时阻断差值。单参数扫描支持朝向、增益与平面位置，逐行列出覆盖、声级范围和背景差，用户确认后才把某个取值应用到项目。方向剖面按起点、角度、长度和步长采样，保留距离、声级、目标状态、内部和截断标记，不用最后满足点推断连续覆盖半径。输入复核表根据来源占位、重合源、近距离和模型边界生成具体提示，不冒充自动诊断。
音频功能支持限定格式和大小的数字波形文件内嵌、校验、移除与数字频谱分析，不因上传录音自动修改物理声压级。使用相对谱与人工声明参考构建频带时，自动标记为估算并记录来源。相对试听采用导入声音或固定种子模拟频带噪声，依照观察点传播结果作七带整形，可叠加背景，统一数字参考、限制峰值并导出单声道文件，不逐文件自动归一化。试听不模拟双耳线索、车身和相干传播，不表示设备已校准。
本机项目保存采用临时写入、刷新、替换和上次版本备份，哈希冲突检查阻止覆盖其他会话的修改。项目文件内嵌频带、音频、快照和审计，支持实际保存重开与搬迁后重算；结构缺字段、未知版本、空对象、无效引用和音频校验失败均拒绝加载。会话支持撤销重做并保留操作审计，成果可输出自包含工程报告、网格及观察点明细、矢量声场图、计算快照、比较和扫描及剖面文件，附带版本、来源、单位、参数和输入摘要。软件采用模型视图视图模型结构，提供解决方案启动入口；不提供法规认证、真实三维车身声场、人体可听性保证或未经验证的实际车辆结论。'''

def setup(doc,kind):
 sec=doc.sections[0];sec.page_width=Mm(210);sec.page_height=Mm(297)
 sec.top_margin=Mm(19);sec.bottom_margin=Mm(19);sec.left_margin=Mm(20);sec.right_margin=Mm(20)
 for key in ['Normal','Title','Heading 1','Heading 2','Caption']:
  style=doc.styles[key];style.font.name='Noto Sans CJK SC';style.element.get_or_add_rPr().rFonts.set(qn('w:eastAsia'),'Noto Sans CJK SC');style.font.color.rgb=RGBColor(0,0,0)
 normal=doc.styles['Normal'];normal.font.size=Pt(10.5);normal.paragraph_format.line_spacing=1.3;normal.paragraph_format.space_after=Pt(6)
 doc.styles['Title'].font.size=Pt(19);doc.styles['Heading 1'].font.size=Pt(16);doc.styles['Heading 2'].font.size=Pt(12)
 sec.header.paragraphs[0].text=NAME+' V1.0  '+kind;sec.header.paragraphs[0].style='Caption'
 footer=sec.footer.paragraphs[0];footer.alignment=2;footer.add_run('V1.0  第 ')
 field=OxmlElement('w:fldSimple');field.set(qn('w:instr'),'PAGE');footer._p.append(field);footer.add_run(' 页')

 for element in list(doc.styles.element.xpath('.//w:pBdr'))+list(doc.element.xpath('.//w:pBdr')):
  element.getparent().remove(element)
 doc.core_properties.author='';doc.core_properties.last_modified_by='';doc.core_properties.title=NAME+' V1.0 '+kind
 doc.core_properties.created=datetime.now(timezone.utc);doc.core_properties.modified=datetime.now(timezone.utc)

def manual():
 doc=Document();setup(doc,'操作说明书');doc.add_heading(NAME+' V1.0 操作说明书',0);md=['# '+NAME+' V1.0 操作说明书',''];figure=0
 for i,page in enumerate(PAGES):
  if i:doc.add_page_break()
  doc.add_heading(page['title'],1);md+=['## '+page['title'],'']
  for para in page['paragraphs']:
   doc.add_paragraph(para);md += [para,'']
  for title,para in page.get('sections',[]):
   doc.add_heading(title,2);doc.add_paragraph(para);md += ['### '+title,'',para,'']
  if page.get('image'):
   image_path=DOCS/'screenshots'/page['image'].replace('.png','-viewport.png')
   if not image_path.exists():image_path=DOCS/'screenshots'/page['image']
   assert image_path.exists(),image_path
   with Image.open(image_path) as image: w,h=image.size
   width=min(170,95*w/h)
   paragraph=doc.add_paragraph();paragraph.alignment=1
   paragraph.add_run().add_picture(str(image_path),width=Mm(width))
   figure+=1;caption=f'图{figure} {page["title"]} 真实运行界面'
   p=doc.add_paragraph(caption,style='Caption');p.alignment=1
   md += [f'![{caption}](screenshots/{image_path.name})','']
 doc.save(DOCS/'操作说明书.docx');(DOCS/'操作说明书.md').write_text('\n'.join(md))

def info():
 doc=Document();setup(doc,'软著信息采集表');doc.add_heading(NAME,0);doc.add_heading('V1.0 软著信息采集表',1)
 fields=[('软件全称',NAME),('软件简称','AVAS Field Studio'),('版本号','V1.0'),('申请人及身份','待申请人核实/填写'),('著作权人及权属','待申请人核实/填写'),('开发完成日期','待申请人核实/填写'),('首次发表及日期','待申请人核实/填写'),('开发方式与权利取得','待申请人核实/填写'),('开发语言','C#；Razor视图；CSS样式'),('开发与运行平台','.NET 10 / ASP.NET Core Blazor Interactive Server；本机服务与现代浏览器。已验证Linux与Chromium，Windows及Visual Studio图形启动待验证。'),('解决方案入口','AvasCoverage.sln，启动项目Avas.App'),('文件格式','.avasproj（UTF-8 JSON格式1，内嵌PCM WAV）；频谱和观察点CSV；HTML、SVG、JSON、WAV成果。'),('运行依赖','.NET SDK 10.0.403或同版本带后续补丁；无第三方NuGet生产依赖。'),('软件技术特点','MVVM分离实体、核心计算及输入输出、可观察视图模型与Razor界面。自由场球面扩散、水平指向性、非相干频带能量叠加、可追溯方案比较、内嵌音频及冲突安全保存。'),('硬件环境','未建立最低性能基准；开发验证机为Linux x64云环境。正式最低配置待目标设备验证后填写。')]
 table=doc.add_table(rows=0,cols=2);table.style='Table Grid';table.autofit=False;table.columns[0].width=Mm(39);table.columns[1].width=Mm(131)
 for key,value in fields:
  row=table.add_row();row.cells[0].text=key;row.cells[1].text=value
  row.cells[0].width=Mm(39);row.cells[1].width=Mm(131)
  for cell in row.cells:
   for p in cell.paragraphs:
    p.paragraph_format.space_after=Pt(4)
 row=table.add_row();row.cells[0].text='软件主要功能（已实现）';row.cells[1].text=''
 for index,para in enumerate(FUNCTIONS.split('\n')):
  paragraph=row.cells[1].paragraphs[0] if index==0 else row.cells[1].add_paragraph()
  paragraph.text=para
 doc.add_paragraph('专用申报模板未提供，本表为可编辑通用采集表。身份、权属及日期不继承其他项目，正式申报时由申请人确认并按当次模板转填。')
 doc.save(DOCS/'软著信息采集表.docx');(DOCS/'软件主要功能.txt').write_text(FUNCTIONS)
 count=len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff\U00020000-\U0002fa1f]',FUNCTIONS))
 assert count>500
 (DOCS/'材料结构检查.json').write_text(json.dumps({'major_function_han_count':count,'manual_sections':len(PAGES),'manual_first_section':PAGES[0]['title'],'dedicated_template_provided':False,'word_pagination_verified':False},ensure_ascii=False,indent=2))
 print('Major function Han characters:',count,'Manual sections:',len(PAGES))
manual();info()
