"""Build editable V2.0 materials from current features and real screenshots."""
import html
import json
import re
from pathlib import Path
from datetime import datetime, timezone
from docx import Document
from docx.shared import Mm,Pt
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT=Path(__file__).resolve().parents[1]
DOCS=ROOT/'docs'
NAME='车身声学泄漏路径与封堵试验分析软件'
PAGES=json.loads((ROOT/'tools/manual-content.json').read_text())
FUNCTIONS='''本软件面向车身声学试验工程人员，用于组织临时封堵前后的测量记录、核查比较条件并输出可回溯分析。软件采用C#和模型视图视图模型结构，提供解决方案启动入口，本机服务和浏览器界面共同形成完整操作环境。用户从空白建立项目，填写负责人、车辆代号、试验目标和说明，可复制为独立标识的副本、归档为只读并解除归档。本机项目库列出实际保存文件及读取状态，支持打开项目和上次备份，数据不依赖最初导入文件路径。
封堵位置管理记录车身区域、材料规格、覆盖范围、封堵工艺、解除封堵与恢复步骤、来源编号和备注，关联试验保持稳定引用，删除仍被引用对象会提示并阻止。工况管理记录车辆、测点、激励、路面、挡位、车速、转速、温度、积分时长、计权、频带制、仪器、标定和声级参考，可选启用采样率及同步定义一致性检查。必要口径缺失可存草稿，但不会生成可比差值。
试验档案区分基线、单项封堵、组合封堵和恢复，检查封堵位置选择数量，保留带时区时间、重复组、操作员、准备状态、来源类别和来源说明。频带支持结构化逐行编辑、备注、选中草稿行移除和两列文本解析，零声级与缺失分别保存，不补零。文件批量导入支持明确分隔符、编码与表头、试验标识校验、逐行错误、重复频率和覆盖范围预检，合并或替换模式经确认整体应用，错误和过期预检不改变旧输入。
用户逐条或显式勾选批量建立基线与目标配对，软件检查类型、引用、工况容差和来源一致性，不可比时保留阻断原因，计算列为空。比较界面提供测得声级差、原始频谱、数值表、频率范围扫描和单项组合矩阵，区分配对差算术均值与组能量平均声级之差。重复性统计列出有效样本、缺失、均值、样本标准差和极差，独立且满足近似正态假设的重复配对可计算均值区间，单样本或复用试验不生成区间。漂移与恢复按显式配对与工程阈值筛查，质量问题提供具体修复建议和定位入口。
原始输入核对表允许选择试验横向检查有效、明确缺失与未声明频带，工况差异会提示。工程规则情景在克隆输入上试算不同参数的状态影响，原项目不变，不推荐放宽规则或改变测量。版本核对按对象标识和频率比较两份项目的新增、移除及修改，显示输入身份，不自动按名称猜测或覆盖工作项目。当前会话支持撤销重做并保留累计审计，用户可记录带参数、阻断原因、修订和输入哈希的复核快照。
保存采用临时写入刷新后替换及旧文件备份，检测外部修改冲突并阻止覆盖。项目内嵌全部频带与来源，可保存、搬迁、重开，旧格式通过明确迁移另存而不改原文件。输出包含自包含分析报告、原始频带、可追溯结果明细、分析快照和辅助核对结果，保留单位、来源、版本、参数和异常原因。演示数列持续标记模拟，实测仅代表用户声明；软件不读取音频、不认证标定和登记事实，不把封堵差解释为路径独立能量贡献，不以单项分贝相加预测组合，也不以工程筛查代替标准合格判断。'''


def style(doc,kind):
    sec=doc.sections[0]
    sec.page_width=Mm(210);sec.page_height=Mm(297)
    sec.top_margin=Mm(18);sec.bottom_margin=Mm(18)
    sec.left_margin=Mm(20);sec.right_margin=Mm(20)
    normal=doc.styles['Normal'];normal.font.name='Noto Sans CJK SC';normal.font.size=Pt(10.5)
    normal.element.rPr.rFonts.set(qn('w:eastAsia'),'Noto Sans CJK SC')
    normal.paragraph_format.line_spacing=1.35
    normal.paragraph_format.space_after=Pt(7)
    for name in ['Title','Heading 1','Heading 2']:
        doc.styles[name].font.name='Noto Sans CJK SC'
        doc.styles[name].element.rPr.rFonts.set(qn('w:eastAsia'),'Noto Sans CJK SC')
    doc.styles['Heading 1'].font.size=Pt(17)
    sec.header.paragraphs[0].text=NAME+' V2.0 · '+kind
    sec.header.paragraphs[0].style=doc.styles['Caption']
    footer=sec.footer.paragraphs[0];footer.alignment=2;footer.add_run('第 ')
    fld=OxmlElement('w:fldSimple');fld.set(qn('w:instr'),'PAGE');footer._p.append(fld)
    footer.add_run(' 页 · V2.0')
    doc.core_properties.author=''
    doc.core_properties.last_modified_by=''
    doc.core_properties.created=datetime.now(timezone.utc)
    doc.core_properties.modified=datetime.now(timezone.utc)
    doc.core_properties.title=NAME+' V2.0 '+kind


def manual():
    doc=Document();style(doc,'操作说明书')
    markdown=['# '+NAME+' V2.0 操作说明书','']
    rendered=[];figure=0
    for index,page in enumerate(PAGES):
        if index:doc.add_page_break()
        doc.add_heading(page['title'],1)
        markdown+=['## '+page['title'],'']
        section=['<section class="sheet"><h1>'+html.escape(page['title'])+'</h1>']
        for paragraph in page['paragraphs']:
            doc.add_paragraph(paragraph);markdown+=[paragraph,'']
            section.append('<p>'+html.escape(paragraph)+'</p>')
        if 'image' in page:
            figure+=1
            image=DOCS/'screenshots'/page['image']
            assert image.is_file(),image
            caption='图'+str(figure)+' '+page['caption']+'（真实运行界面；数据模拟）'
            doc.add_picture(str(image),width=Mm(166));doc.add_paragraph(caption,style='Caption')
            markdown += ['!['+caption+'](screenshots/'+page['image']+')','']
            section.append('<figure><img src="screenshots/'+html.escape(page['image'])+'"><figcaption>'+html.escape(caption)+'</figcaption></figure>')
        section.append('<div class="folio">'+NAME+' V2.0 · 操作说明书 · '+str(index+1)+'</div></section>')
        rendered.append(''.join(section))
    doc.save(DOCS/'操作说明书.docx')
    (DOCS/'操作说明书.md').write_text('\n'.join(markdown).rstrip()+'\n')
    css='''@page{size:A4;margin:0}*{box-sizing:border-box}body{margin:0;font-family:"Noto Sans CJK SC",sans-serif;color:#213b4c}.sheet{width:210mm;height:297mm;padding:18mm 20mm;break-after:page;position:relative;overflow:hidden}.sheet:last-child{break-after:auto}h1{font-size:17pt;border-bottom:2px solid #087d78;padding-bottom:8px;margin:0 0 14px}p{font-size:10.5pt;line-height:1.65;margin:0 0 10px;text-align:justify}figure{margin:12px 0 0}img{width:166mm;display:block;border:1px solid #dbe4e7}figcaption{font-size:8.5pt;color:#587282;margin-top:5px}.folio{position:absolute;bottom:10mm;left:20mm;right:20mm;border-top:1px solid #dbe4e7;padding-top:6px;font-size:8pt;color:#657d8c}'''
    (DOCS/'操作说明书-预览.html').write_text('<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>V2.0操作说明书预览</title><style>'+css+'</style>'+''.join(rendered)+'</html>')
    return figure


def information():
    count=len(re.findall(r'[\u3400-\u4dbf\u4e00-\u9fff]',FUNCTIONS));assert count>500
    info=json.loads((DOCS/'source-inventory.json').read_text())
    rows=[
        ('软件全称',NAME),('版本号','V2.0；内部2.0.0；项目schema2'),
        ('软件简称','待申请人核实/填写（程序未声明简称）'),
        ('申请人、国籍、证件和联系方式','待申请人核实/填写'),
        ('著作权人、权属来源及开发方式','待申请人核实/填写'),
        ('开发完成日期','待申请人核实/填写；不以本次生成时间代替'),
        ('首次发表状态、日期、地点','待申请人核实/填写；公开仓库推送与法定发表口径须分别核实'),
        ('语言与工程','C#；Razor视图及CSS样式；AcousticSeal.sln启动入口；MVVM'),
        ('运行环境','.NET10/ASP.NET Core本机服务与现代浏览器；Linux/Chromium已运行，Windows/macOS和Visual Studio图形启动未执行'),
        ('开发工具与依赖','.NET SDK10.0.403；生产无第三方NuGet依赖；可选Python材料工具不属于软件生产依赖'),
        ('主要功能',FUNCTIONS),('源程序量',str(info['productionNonblank'])+'手写生产非空行；其中C#'+str(info['byExtension']['.cs'])+'行；不含测试、生成、工具、文档和示例'),
        ('文件格式','.sealproj（schema2）；旧.aslp.json明确迁移；CSV/HTML/辅助分析JSON'),
        ('模板与申报范围','未收到申请人专用模板，本表为通用可编辑采集稿；当次字段顺序、合并单元格、边框、源码页数及签章待复核')
    ]
    doc=Document();style(doc,'软著信息采集表')
    doc.add_heading('软著信息采集表（申请人复核稿）',1)
    doc.add_paragraph('主要功能按Unicode汉字统计为'+str(count)+'字。身份、权属和日期等未知事实不代填。')
    table=doc.add_table(rows=0,cols=2);table.style='Table Grid'
    table.autofit=False
    table.columns[0].width=Mm(37)
    table.columns[1].width=Mm(133)
    for key,value in rows:
        cells=table.add_row().cells;cells[0].width=Mm(37);cells[1].width=Mm(133);cells[0].text=key;cells[1].text=value
    doc.save(DOCS/'软著信息采集表.docx')
    markdown=['# V2.0软著信息采集表','',f'主要功能Unicode汉字数：{count}（大于500）。','']
    for key,value in rows:markdown+=['## '+key,'',value,'']
    (DOCS/'软著信息采集表.md').write_text('\n'.join(markdown).rstrip()+'\n')
    return count


def source():
    doc=Document();style(doc,'完整生产源码排印稿')
    normal=doc.styles['Normal'];normal.font.size=Pt(7)
    normal.paragraph_format.line_spacing=Pt(9);normal.paragraph_format.space_after=Pt(0)
    metadata=json.loads((DOCS/'source-inventory.json').read_text());first=True
    for item in metadata['files']:
        lines=(ROOT/item['path']).read_text().splitlines()
        for start in range(0,len(lines),50):
            if not first:doc.add_page_break()
            first=False;doc.add_heading(item['path']+f' · 行{start+1}—{min(start+50,len(lines))}',2)
            for index,line in enumerate(lines[start:start+50],start+1):
                run=doc.add_paragraph().add_run(f'{index:04d}  {line}')
                run.font.name='DejaVu Sans Mono';run.font.size=Pt(7)
                run.element.rPr.rFonts.set(qn('w:eastAsia'),'Noto Sans CJK SC')
    doc.save(DOCS/'完整生产源码排印稿.docx')


if __name__=='__main__':
    figures=manual();han=information();source()
    (DOCS/'material-build.json').write_text(json.dumps({'manualSections':len(PAGES),'manualFigures':figures,'functionHanCount':han,'wordPagination':'未执行，需Microsoft Word实际验证','template':'未提供专用模板'},ensure_ascii=False,indent=2))
    print(json.dumps({'manualSections':len(PAGES),'figures':figures,'functionHanCount':han},ensure_ascii=False))
